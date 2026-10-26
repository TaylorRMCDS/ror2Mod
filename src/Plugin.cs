using BepInEx;

namespace TaylorsBalancePatch;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.t4ylor.ror2.balancepatch";
    public const string PluginName = "T4ylorSurvivors";
    public const string PluginVersion = "1.1.1";

    private CommandoChanges? commando;
    private ArtificerChanges? artificer;
    private BanditChanges? bandit;
    private CaptainChanges? captain;
    private HuntressChanges? huntress;
    private RexChanges? rex;
    private MultChanges? mult;
    private EngineerChanges? engineer;
    private LoaderChanges? loader;
    private AcridChanges? acrid;
    private MercenaryChanges? mercenary;
    private RailgunnerChanges? railgunner;
    private VoidFiendChanges? voidFiend;
    private FalseSonChanges? falseSon;
    private SeekerChanges? seeker;
    private ChefChanges? chef;
    private DrifterChanges? drifter;
    private OperatorChanges? operatorChanges;

    private void Awake()
    {
        MercenaryChanges.RegisterContent(Logger);
        ChangeGroups.Bind(Config);

        if (ChangeGroups.Any("Commando"))
        {
            commando = new CommandoChanges(Logger);
            commando.Enable();
        }
        else Logger.LogInfo("Commando changes DISABLED.");

        if (ChangeGroups.Any("Artificer"))
        {
            artificer = new ArtificerChanges(Logger);
            artificer.Enable();
        }
        else Logger.LogInfo("Artificer changes DISABLED.");

        if (ChangeGroups.Any("Bandit"))
        {
            bandit = new BanditChanges(Logger);
            bandit.Enable();
        }
        else Logger.LogInfo("Bandit changes DISABLED.");

        if (ChangeGroups.Any("Captain"))
        {
            captain = new CaptainChanges(Logger);
            captain.Enable();
        }
        else Logger.LogInfo("Captain changes DISABLED.");

        if (ChangeGroups.Any("Huntress"))
        {
            huntress = new HuntressChanges(Logger);
            huntress.Enable();
        }
        else Logger.LogInfo("Huntress changes DISABLED.");

        if (ChangeGroups.Any("REX"))
        {
            rex = new RexChanges(Logger);
            rex.Enable();
        }
        else Logger.LogInfo("REX changes DISABLED.");

        if (ChangeGroups.Any("MUL-T"))
        {
            mult = new MultChanges(Logger);
            mult.Enable();
        }
        else Logger.LogInfo("MUL-T changes DISABLED.");

        if (ChangeGroups.Any("Engineer"))
        {
            engineer = new EngineerChanges(Logger);
            engineer.Enable();
        }
        else Logger.LogInfo("Engineer changes DISABLED.");

        if (ChangeGroups.Any("Loader"))
        {
            loader = new LoaderChanges(Logger);
            loader.Enable();
        }
        else Logger.LogInfo("Loader changes DISABLED.");

        if (ChangeGroups.Any("Acrid"))
        {
            acrid = new AcridChanges(Logger);
            acrid.Enable();
        }
        else Logger.LogInfo("Acrid changes DISABLED.");

        if (ChangeGroups.Any("Mercenary"))
        {
            mercenary = new MercenaryChanges(Logger);
            mercenary.Enable();
        }
        else Logger.LogInfo("Mercenary changes DISABLED.");
        if (ChangeGroups.Any("Railgunner"))
        {
            railgunner = new RailgunnerChanges(Logger);
            railgunner.Enable();
        }
        else Logger.LogInfo("Railgunner changes DISABLED.");
        if (ChangeGroups.Any("Void Fiend"))
        {
            voidFiend = new VoidFiendChanges(Logger);
            voidFiend.Enable();
        }
        else Logger.LogInfo("Void Fiend changes DISABLED.");
        if (ChangeGroups.Any("False Son"))
        {
            falseSon = new FalseSonChanges(Logger);
            falseSon.Enable();
        }
        else Logger.LogInfo("False Son changes DISABLED.");
        if (ChangeGroups.Any("Seeker"))
        {
            seeker = new SeekerChanges(Logger);
            seeker.Enable();
        }
        else Logger.LogInfo("Seeker changes DISABLED.");
        if (ChangeGroups.Any("CHEF"))
        {
            chef = new ChefChanges(Logger);
            chef.Enable();
        }
        else Logger.LogInfo("CHEF changes DISABLED.");
        if (ChangeGroups.Any("Drifter"))
        {
            drifter = new DrifterChanges(Logger);
            drifter.Enable();
        }
        else Logger.LogInfo("Drifter changes DISABLED.");
        if (ChangeGroups.Any("Operator"))
        {
            operatorChanges = new OperatorChanges(Logger);
            operatorChanges.Enable();
        }
        else Logger.LogInfo("Operator changes DISABLED.");
    }

    private void OnDestroy()
    {
        commando?.Dispose();
        artificer?.Dispose();
        bandit?.Dispose();
        captain?.Dispose();
        huntress?.Dispose();
        rex?.Dispose();
        mult?.Dispose();
        engineer?.Dispose();
        loader?.Dispose();
        acrid?.Dispose();
        mercenary?.Dispose();
        railgunner?.Dispose();
        voidFiend?.Dispose();
        falseSon?.Dispose();
        seeker?.Dispose();
        chef?.Dispose();
        drifter?.Dispose();
        operatorChanges?.Dispose();
    }
}
