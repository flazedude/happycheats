namespace HappyCheats.Core;

public static class ModuleCatalog
{
    public static IReadOnlyList<IGameModule> Create(string? baseDirectory = null) => new IGameModule[]
    {
        new NomadDriveModule(baseDirectory ?? AppContext.BaseDirectory),
        new ChineseParentsModule(baseDirectory ?? AppContext.BaseDirectory),
        new ProjectHospitalModule(baseDirectory ?? AppContext.BaseDirectory),
        new HotelArchitectModule(baseDirectory ?? AppContext.BaseDirectory),
        new CivilizationVIModule(baseDirectory ?? AppContext.BaseDirectory),
        new CivilizationVIIModule(baseDirectory ?? AppContext.BaseDirectory),
        new EldenRingModule(baseDirectory ?? AppContext.BaseDirectory),
        new OnimushaModule(baseDirectory ?? AppContext.BaseDirectory),
        new LaundryModule(baseDirectory ?? AppContext.BaseDirectory),
        new AstroneerModule(baseDirectory ?? AppContext.BaseDirectory),
        new StardewModule(baseDirectory ?? AppContext.BaseDirectory),
        new DawnwalkerModule(baseDirectory ?? AppContext.BaseDirectory),
        new CrimsonDesertModule(baseDirectory ?? AppContext.BaseDirectory),
        new WukongModule(baseDirectory ?? AppContext.BaseDirectory),
        new PhantomBladeModule(baseDirectory ?? AppContext.BaseDirectory),
        new Rdr2Module(baseDirectory ?? AppContext.BaseDirectory),
        new GtaVModule(baseDirectory ?? AppContext.BaseDirectory),
        new Witcher3Module(baseDirectory ?? AppContext.BaseDirectory)
    };
}
