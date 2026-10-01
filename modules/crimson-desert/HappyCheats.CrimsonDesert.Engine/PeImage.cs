using System.Buffers.Binary;
using System.Text;

namespace HappyCheats.CrimsonDesert;

public sealed record PeSection(string Name, uint VirtualSize, uint VirtualAddress, uint SizeOfRawData,
    uint PointerToRawData, uint Characteristics)
{
    public bool IsExecutable => (Characteristics & 0x20000000) != 0;

    /// <summary>Actual file bytes inside the declared virtual section, excluding alignment padding.</summary>
    public uint RawBackedSize => VirtualSize == 0 ? SizeOfRawData : Math.Min(VirtualSize, SizeOfRawData);
}

public sealed record PePatternMatch(uint Rva, ulong VirtualAddress, PeSection Section, int SectionOffset);

/// <summary>
/// Read-only AMD64 PE32+ inspection. RVAs address the image, never file offsets.
/// This class does not open processes, load executable code, or modify its input.
/// </summary>
public sealed class PeImage
{
    private readonly byte[] data;
    private readonly uint sizeOfHeaders;

    public ulong ImageBase { get; }
    public uint SizeOfImage { get; }
    public IReadOnlyList<PeSection> Sections { get; }

    public PeImage(byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);
        data = (byte[])image.Clone();
        RequireFileRange(0, 64, "DOS header");
        if (U16(0) != 0x5a4d) throw Invalid("Missing DOS MZ signature.");
        var peOffset = U32(0x3c);
        if (peOffset < 64) throw Invalid("PE header overlaps the DOS header.");
        RequireFileRange(peOffset, 24, "PE signature and COFF header");
        var pe = checked((int)peOffset);
        if (U32(pe) != 0x00004550) throw Invalid("Missing PE signature.");
        if (U16(pe + 4) != 0x8664) throw Invalid("Only AMD64 images are supported.");
        var sectionCount = U16(pe + 6);
        if (sectionCount is 0 or > 96) throw Invalid("Invalid PE section count.");
        var optionalSize = U16(pe + 20);
        if (optionalSize < 112) throw Invalid("Truncated PE32+ optional header.");
        var optionalOffset = (ulong)peOffset + 24;
        RequireFileRange(optionalOffset, optionalSize, "optional header");
        var optional = checked((int)optionalOffset);
        if (U16(optional) != 0x20b) throw Invalid("Only PE32+ optional headers are supported.");
        ImageBase = U64(optional + 24);
        SizeOfImage = U32(optional + 56);
        sizeOfHeaders = U32(optional + 60);
        if (SizeOfImage == 0 || sizeOfHeaders == 0 || sizeOfHeaders > SizeOfImage)
            throw Invalid("Invalid image or header size.");
        if (ImageBase > ulong.MaxValue - SizeOfImage) throw Invalid("Image address range overflows.");
        RequireFileRange(0, sizeOfHeaders, "image headers");
        var directoryCount = U32(optional + 108);
        if ((ulong)directoryCount * 8 > (ulong)optionalSize - 112)
            throw Invalid("Data directories exceed the optional header.");

        var sectionTable = optionalOffset + optionalSize;
        var sectionTableSize = (ulong)sectionCount * 40;
        RequireFileRange(sectionTable, sectionTableSize, "section table");
        if (sectionTable + sectionTableSize > sizeOfHeaders) throw Invalid("Section table exceeds image headers.");

        var sections = new List<PeSection>(sectionCount);
        for (var i = 0; i < sectionCount; i++)
        {
            var offset = checked((int)(sectionTable + (ulong)i * 40));
            var nameBytes = data.AsSpan(offset, 8);
            var nameEnd = nameBytes.IndexOf((byte)0);
            var section = new PeSection(Encoding.ASCII.GetString(nameEnd < 0 ? nameBytes : nameBytes[..nameEnd]),
                U32(offset + 8), U32(offset + 12), U32(offset + 16), U32(offset + 20), U32(offset + 36));
            var extent = Math.Max(section.VirtualSize, section.SizeOfRawData);
            if (extent > 0 && (section.VirtualAddress < sizeOfHeaders ||
                              (ulong)section.VirtualAddress + extent > SizeOfImage))
                throw Invalid($"Section '{section.Name}' exceeds the image or overlaps its headers.");
            if (section.SizeOfRawData > 0)
            {
                if (section.PointerToRawData < sizeOfHeaders) throw Invalid($"Section '{section.Name}' overlaps file headers.");
                RequireFileRange(section.PointerToRawData, section.SizeOfRawData, $"section '{section.Name}' raw data");
            }
            foreach (var prior in sections)
            {
                if (Overlaps(section.VirtualAddress, extent, prior.VirtualAddress, Math.Max(prior.VirtualSize, prior.SizeOfRawData)))
                    throw Invalid("Virtual sections overlap; RVA mapping would be ambiguous.");
                if (Overlaps(section.PointerToRawData, section.SizeOfRawData, prior.PointerToRawData, prior.SizeOfRawData))
                    throw Invalid("Raw section data overlaps; RVA mapping would be ambiguous.");
            }
            sections.Add(section);
        }
        Sections = sections.AsReadOnly();
    }

    /// <summary>
    /// Return a copy of a wholly raw-backed RVA range. Virtual zero-fill, section padding,
    /// gaps and ranges crossing a header/section boundary are rejected.
    /// </summary>
    public byte[] ReadRva(uint rva, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if ((ulong)rva + (uint)count > SizeOfImage) throw Invalid("RVA range exceeds the image.");
        if (count == 0) return Array.Empty<byte>();
        if (rva < sizeOfHeaders)
        {
            if ((ulong)rva + (uint)count > sizeOfHeaders) throw Invalid("RVA range crosses the header boundary.");
            return data.AsSpan(checked((int)rva), count).ToArray();
        }
        foreach (var section in Sections)
        {
            if (rva < section.VirtualAddress) continue;
            var relative = (ulong)rva - section.VirtualAddress;
            if (relative >= section.RawBackedSize) continue;
            if (relative + (uint)count > section.RawBackedSize) throw Invalid("RVA range crosses a raw-backed section boundary.");
            return data.AsSpan(checked((int)(section.PointerToRawData + relative)), count).ToArray();
        }
        throw Invalid("RVA has no file-backed data.");
    }

    /// <summary>Scan each executable section independently; patterns never span sections.</summary>
    public List<PePatternMatch> FindExecutableMatches(BinaryPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var matches = new List<PePatternMatch>();
        foreach (var section in Sections.Where(section => section.IsExecutable && section.RawBackedSize > 0))
        {
            var bytes = data.AsSpan(checked((int)section.PointerToRawData), checked((int)section.RawBackedSize));
            foreach (var offset in pattern.FindAll(bytes))
            {
                var rva = checked(section.VirtualAddress + (uint)offset);
                matches.Add(new PePatternMatch(rva, ImageBase + rva, section, offset));
            }
        }
        return matches;
    }

    /// <summary>Require exactly one executable match. Missing and ambiguous signatures fail closed.</summary>
    public PePatternMatch FindUniqueExecutableMatch(BinaryPattern pattern)
    {
        var matches = FindExecutableMatches(pattern);
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException("The pattern has no executable-section match."),
            _ => throw new InvalidOperationException($"The pattern is ambiguous: {matches.Count} executable-section matches.")
        };
    }

    private void RequireFileRange(ulong offset, ulong count, string description)
    {
        if (offset > (ulong)data.Length || count > (ulong)data.Length - offset)
            throw Invalid($"Truncated or out-of-bounds {description}.");
    }

    private static bool Overlaps(uint first, uint firstSize, uint second, uint secondSize) =>
        firstSize > 0 && secondSize > 0 && (ulong)first < (ulong)second + secondSize && (ulong)second < (ulong)first + firstSize;

    private ushort U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    private uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    private ulong U64(int offset) => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset, 8));
    private static InvalidDataException Invalid(string message) => new(message);
}
