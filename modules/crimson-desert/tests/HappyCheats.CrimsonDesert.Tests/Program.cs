using System.Buffers.Binary;
using HappyCheats.CrimsonDesert;

var passed = 0;
var failed = 0;

Run("whole-byte wildcards and whitespace", () => Equal(BinaryPattern.Parse(" \t48 ?? 8b ?\r\n").FindAll(new byte[] { 0x48, 0, 0x8b, 0xfe }), 0));
Run("literal zero is not a wildcard", () => Equal(BinaryPattern.Parse("00 01").FindAll(new byte[] { 7, 1, 0, 1 }), 2));
Run("overlapping and final-position matches", () => Equal(BinaryPattern.Parse("AA aa").FindAll(new byte[] { 0xaa, 0xaa, 0xaa }), 0, 1));
Run("empty input and oversized pattern have no matches", () => {
    Equal(BinaryPattern.Parse("00").FindAll(Array.Empty<byte>()));
    Equal(BinaryPattern.Parse("00 00").FindAll(new byte[] { 0 }));
});
Run("all-wildcard pattern requires complete width", () => Equal(BinaryPattern.Parse("? ??").FindAll(new byte[] { 3, 4, 5 }), 0, 1));
Run("zero matches", () => Equal(BinaryPattern.Parse("FF 12").FindAll(new byte[] { 0xff, 0x11, 0xff })));
Run("malformed patterns are rejected", () => {
    foreach (var text in new[] { "", " \r\n", "0", "GG", "1FF", "0x12", "A?", "?A", "12,34", "-1", "+1" })
        Reject<FormatException>(() => BinaryPattern.Parse(text));
    Reject<ArgumentNullException>(() => BinaryPattern.Parse(null!));
});
Run("PE metadata and file-backed RVA mapping", () => {
    var image = new PeImage(Fixture());
    Check(image.ImageBase == 0x140000000 && image.SizeOfImage == 0x5000 && image.Sections.Count == 3);
    Check(image.Sections[0].Name == ".text" && image.Sections[0].IsExecutable && !image.Sections[1].IsExecutable);
    Equal(image.ReadRva(0, 2).Select(value => (int)value), 0x4d, 0x5a);
    Equal(image.ReadRva(0x1020, 4).Select(value => (int)value), 0x48, 0x89, 0x42, 0x90);
});
Run("constructor and returned data have independent ownership", () => {
    var bytes = Fixture(); var image = new PeImage(bytes); bytes[0x220] = 0;
    var read = image.ReadRva(0x1020, 4); read[0] = 0;
    Check(image.ReadRva(0x1020, 1)[0] == 0x48);
});
Run("executable scan skips data and reports RVA and preferred VA", () => {
    var image = new PeImage(Fixture());
    var match = image.FindUniqueExecutableMatch(BinaryPattern.Parse("48 89 ?? 90"));
    Check(match.Rva == 0x1020 && match.VirtualAddress == 0x140001020 && match.SectionOffset == 0x20 && match.Section.Name == ".text");
});
Run("unique scan refuses zero or ambiguous matches", () => {
    var bytes = Fixture(); var image = new PeImage(bytes);
    Reject<InvalidOperationException>(() => image.FindUniqueExecutableMatch(BinaryPattern.Parse("AB CD EF")));
    new byte[] { 0x48, 0x89, 0x55, 0x90 }.CopyTo(bytes, 0x630);
    image = new PeImage(bytes);
    Equal(image.FindExecutableMatches(BinaryPattern.Parse("48 89 ?? 90")).Select(match => (int)match.Rva), 0x1020, 0x3030);
    Reject<InvalidOperationException>(() => image.FindUniqueExecutableMatch(BinaryPattern.Parse("48 89 ?? 90")));
});
Run("signature cannot cross even adjacent executable sections", () => {
    var bytes = Fixture(); U32(bytes, Section(1) + 12, 0x1200); U32(bytes, Section(1) + 36, 0x60000020);
    bytes[0x3fe] = 0xfa; bytes[0x3ff] = 0xfb; bytes[0x400] = 0xfc; bytes[0x401] = 0xfd;
    var image = new PeImage(bytes);
    Check(image.FindExecutableMatches(BinaryPattern.Parse("FA FB FC FD")).Count == 0);
    Reject<InvalidDataException>(() => image.ReadRva(0x11fe, 4));
});
Run("final bytes match but raw alignment padding is excluded", () => {
    var bytes = Fixture(); U32(bytes, Section(0) + 8, 0x100);
    bytes[0x2fe] = 0xaa; bytes[0x2ff] = 0xbb; bytes[0x300] = 0xcc;
    var image = new PeImage(bytes);
    Check(image.FindUniqueExecutableMatch(BinaryPattern.Parse("AA BB")).Rva == 0x10fe);
    Check(image.FindExecutableMatches(BinaryPattern.Parse("BB CC")).Count == 0);
    Reject<InvalidDataException>(() => image.ReadRva(0x1100, 1));
});
Run("virtual zero-fill and unmapped gaps are never read from disk", () => {
    var bytes = Fixture(); U32(bytes, Section(0) + 8, 0x300);
    var image = new PeImage(bytes);
    Reject<InvalidDataException>(() => image.ReadRva(0x1200, 1));
    Reject<InvalidDataException>(() => image.ReadRva(0x1800, 1));
    Reject<InvalidDataException>(() => image.ReadRva(0x11ff, 2));
    Reject<InvalidDataException>(() => image.ReadRva(0x1ff, 2));
});
Run("empty raw section and zero virtual size are handled explicitly", () => {
    var bytes = Fixture(); U32(bytes, Section(0) + 8, 0);
    U32(bytes, Section(2) + 16, 0); U32(bytes, Section(2) + 20, 0);
    var image = new PeImage(bytes);
    Check(image.FindUniqueExecutableMatch(BinaryPattern.Parse("48 89 ?? 90")).Rva == 0x1020);
    Reject<InvalidDataException>(() => image.ReadRva(0x3000, 1));
});
Run("RVA lengths and arithmetic boundaries fail closed", () => {
    var image = new PeImage(Fixture());
    Check(image.ReadRva(0x5000, 0).Length == 0);
    Reject<ArgumentOutOfRangeException>(() => image.ReadRva(0x1000, -1));
    Reject<InvalidDataException>(() => image.ReadRva(0x5000, 1));
    Reject<InvalidDataException>(() => image.ReadRva(uint.MaxValue, int.MaxValue));
    Reject<InvalidDataException>(() => image.ReadRva(uint.MaxValue, 0));
});
Run("truncated input headers and data are rejected", () => {
    foreach (var length in new[] { 0, 1, 63, 64, 0x83, 0x97, 0x98, 0x107, 0x1ff, 0x400, 0x7ff })
        Reject<InvalidDataException>(() => new PeImage(Fixture()[..length]));
    Reject<ArgumentNullException>(() => new PeImage(null!));
});
Run("signatures, architecture and optional-header magic are validated", () => {
    Mutate(bytes => U16(bytes, 0, 0));
    Mutate(bytes => U32(bytes, 0x80, 0));
    Mutate(bytes => U16(bytes, 0x84, 0x14c));
    Mutate(bytes => U16(bytes, 0x98, 0x10b));
});
Run("header offsets, counts and data-directory bounds are validated", () => {
    Mutate(bytes => U32(bytes, 0x3c, 0x20));
    Mutate(bytes => U32(bytes, 0x3c, uint.MaxValue));
    Mutate(bytes => U16(bytes, 0x86, 0));
    Mutate(bytes => U16(bytes, 0x86, 97));
    Mutate(bytes => U16(bytes, 0x94, 111));
    Mutate(bytes => U16(bytes, 0x94, ushort.MaxValue));
    Mutate(bytes => U32(bytes, 0x98 + 108, 17));
    Mutate(bytes => U32(bytes, 0x98 + 108, uint.MaxValue));
    Mutate(bytes => U32(bytes, 0x98 + 60, 0x100));
});
Run("image and header sizes cannot overflow or exceed the file", () => {
    Mutate(bytes => U32(bytes, 0x98 + 56, 0));
    Mutate(bytes => U32(bytes, 0x98 + 60, 0));
    Mutate(bytes => U32(bytes, 0x98 + 60, 0x6000));
    Mutate(bytes => U32(bytes, 0x98 + 60, 0x1000));
    Mutate(bytes => U64(bytes, 0x98 + 24, ulong.MaxValue));
});
Run("section raw and virtual bounds are validated without wrapping", () => {
    Mutate(bytes => U32(bytes, Section(0) + 12, 0x100));
    Mutate(bytes => U32(bytes, Section(0) + 12, 0x4f00));
    Mutate(bytes => U32(bytes, Section(0) + 12, uint.MaxValue));
    Mutate(bytes => U32(bytes, Section(0) + 8, uint.MaxValue));
    Mutate(bytes => U32(bytes, Section(0) + 20, 0x100));
    Mutate(bytes => U32(bytes, Section(0) + 20, uint.MaxValue));
    Mutate(bytes => U32(bytes, Section(0) + 16, uint.MaxValue));
});
Run("overlapping raw or virtual section mappings are rejected", () => {
    Mutate(bytes => U32(bytes, Section(1) + 12, 0x1100));
    Mutate(bytes => U32(bytes, Section(1) + 20, 0x300));
});

Console.WriteLine($"Crimson Desert binary inspection tests: {passed} passed, {failed} failed. No game process opened.");
return failed == 0 ? 0 : 1;

void Run(string name, Action test)
{
    try { test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
}
static void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
static void Equal(IEnumerable<int> actual, params int[] expected)
{
    var values = actual.ToArray();
    if (!values.SequenceEqual(expected)) throw new Exception($"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", values)}].");
}
static void Reject<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name + ".");
}
static void Mutate(Action<byte[]> mutation)
{
    var bytes = Fixture(); mutation(bytes); Reject<InvalidDataException>(() => new PeImage(bytes));
}
static int Section(int index) => 0x188 + 40 * index;
static void U16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), value);
static void U32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
static void U64(byte[] bytes, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset, 8), value);
static byte[] Fixture()
{
    var bytes = new byte[0x800];
    U16(bytes, 0, 0x5a4d); U32(bytes, 0x3c, 0x80); U32(bytes, 0x80, 0x00004550);
    U16(bytes, 0x84, 0x8664); U16(bytes, 0x86, 3); U16(bytes, 0x94, 0xf0); U16(bytes, 0x98, 0x20b);
    U64(bytes, 0x98 + 24, 0x140000000); U32(bytes, 0x98 + 32, 0x1000); U32(bytes, 0x98 + 36, 0x200);
    U32(bytes, 0x98 + 56, 0x5000); U32(bytes, 0x98 + 60, 0x200); U32(bytes, 0x98 + 108, 16);
    for (var i = 0; i < 3; i++)
    {
        var offset = Section(i);
        System.Text.Encoding.ASCII.GetBytes(i == 1 ? ".data" : i == 0 ? ".text" : ".code").CopyTo(bytes, offset);
        U32(bytes, offset + 8, 0x200); U32(bytes, offset + 12, 0x1000u * ((uint)i + 1));
        U32(bytes, offset + 16, 0x200); U32(bytes, offset + 20, 0x200u * ((uint)i + 1));
        U32(bytes, offset + 36, i == 1 ? 0xc0000040u : 0x60000020u);
    }
    new byte[] { 0x48, 0x89, 0x42, 0x90 }.CopyTo(bytes, 0x220);
    new byte[] { 0x48, 0x89, 0x77, 0x90 }.CopyTo(bytes, 0x420);
    return bytes;
}
