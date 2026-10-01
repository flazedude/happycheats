// HappyCheats Witcher 3 trainer 0.1.1. Original script; no vanilla source bundled.
// This object is deliberately not saved. Effects are refills, not saved cheat facts.
class HappyCheatsWitcher3
{
    private var player : W3PlayerWitcher;
    private var session : string;
    private var lastRequest : string;
    private var recentRequests : array<string>;
    private var lastSuccess : bool;
    private var lastMessage : string;
    private var nextPoll : float;
    private var leaseUntil : float;
    private var health, stamina, adrenaline, toxicity : bool;

    private function Flag(value : bool) : string
    {
        if(value) return "1";
        return "0";
    }

    private function Disable()
    {
        health = false;
        stamina = false;
        adrenaline = false;
        toxicity = false;
    }

    public function Tick()
    {
        var now : float;
        var current : W3PlayerWitcher;
        var ready : bool;
        var bridgeReady : bool;
        var rows : C2dArray;
        var issued : float;
        var snapshot : string;

        now = EngineTimeToFloat(theGame.GetEngineTime());
        current = GetWitcherPlayer();
        if(session == "" || current != player)
        {
            Disable();
            player = current;
            session = IntToString(RandRange(1000000000)) + "-" + FloatToString(now);
            lastRequest = "";
            recentRequests.Clear();
            lastMessage = "session";
            lastSuccess = false;
            leaseUntil = 0.0;
            nextPoll = 0.0;
        }
        ready = player && player.IsAlive() && player.GetStat(BCS_Vitality) > 0.0 && !theGame.IsBlackscreen();
        if(now > leaseUntil) Disable();
        if(ready)
        {
            if(health) player.ForceSetStat(BCS_Vitality, player.GetStatMax(BCS_Vitality));
            if(stamina) player.ForceSetStat(BCS_Stamina, player.GetStatMax(BCS_Stamina));
            if(adrenaline) player.ForceSetStat(BCS_Focus, player.GetStatMax(BCS_Focus));
            if(toxicity) player.DrainToxicity(player.GetStatMax(BCS_Toxicity));
        }
        if(now < nextPoll) return;
        nextPoll = now + 0.25;
        rows = LoadCSVDirect("__HC_BRIDGE_PATH__/lease.csv", ",");
        bridgeReady = rows != null;
        if(rows && rows.GetNumRows() == 1 && rows.GetValue("session", 0) == session)
        {
            issued = StringToFloat(rows.GetValue("issued", 0), -100.0);
            if(issued <= now + 1.0 && now - issued <= 3.0) leaseUntil = issued + 6.0;
        }
        rows = LoadCSVDirect("__HC_BRIDGE_PATH__/command.csv", ",");
        bridgeReady = bridgeReady && rows != null;
        if(rows && rows.GetNumRows() == 1 && rows.GetValue("schema", 0) == "1" && rows.GetValue("session", 0) == session)
        {
            issued = StringToFloat(rows.GetValue("issued", 0), -100.0);
            if(rows.GetValue("request", 0) != "" && recentRequests.FindFirst(rows.GetValue("request", 0)) < 0 && issued <= now + 1.0 && now - issued <= 3.0)
            {
                lastRequest = rows.GetValue("request", 0);
                recentRequests.PushBack(lastRequest);
                if(recentRequests.Size() > 32) recentRequests.Erase(0);
                lastSuccess = Apply(rows.GetValue("action", 0), rows.GetValue("enabled", 0), ready, now);
            }
        }
        if(!bridgeReady) lastMessage = "bridge_unavailable";
        else if(lastMessage == "bridge_unavailable") lastMessage = "connected";
        snapshot = "HCW3|1|0.1.1|" + session + "|" + Flag(ready) + "|" + FloatToString(now)
            + "|" + Flag(health) + "|" + Flag(stamina) + "|" + Flag(adrenaline) + "|" + Flag(toxicity);
        if(ready)
            snapshot += "|" + FloatToString(player.GetStat(BCS_Vitality)) + "|" + FloatToString(player.GetStat(BCS_Stamina)) + "|" + IntToString(player.GetMoney());
        else
            snapshot += "|0|0|0";
        snapshot += "|" + lastRequest + "|" + Flag(lastSuccess) + "|" + lastMessage;
        theGame.GetInGameConfigWrapper().WriteIniFile("modsHappyCheatsWitcher3", "Snapshot", snapshot);
    }

    private function Apply(action : string, enabled : string, ready : bool, now : float) : bool
    {
        var on : bool;
        var toggle : bool;
        var pointsBefore : int;

        lastMessage = "invalid";
        if(action == "disable_all")
        {
            if(enabled != "") return false;
            Disable();
            lastMessage = "disabled";
            return true;
        }
        toggle = action == "witcher3_health" || action == "witcher3_stamina" || action == "witcher3_adrenaline" || action == "witcher3_toxicity";
        if(toggle && enabled != "0" && enabled != "1") return false;
        if(!toggle && enabled != "") return false;
        on = enabled == "1";
        if((!toggle || on) && (!ready || now > leaseUntil))
        {
            lastMessage = "not_ready";
            return false;
        }
        if(action == "witcher3_health") health = on;
        else if(action == "witcher3_stamina") stamina = on;
        else if(action == "witcher3_adrenaline") adrenaline = on;
        else if(action == "witcher3_toxicity") toxicity = on;
        else if(action == "witcher3_heal")
        {
            player.ForceSetStat(BCS_Vitality, player.GetStatMax(BCS_Vitality));
            player.ForceSetStat(BCS_Stamina, player.GetStatMax(BCS_Stamina));
        }
        else if(action == "witcher3_money")
        {
            if(player.GetMoney() > 1000000000) return false;
            player.AddMoney(1000);
        }
        else if(action == "witcher3_xp")
        {
            pointsBefore = player.GetPointsTotal(EExperiencePoint);
            player.AddPoints(EExperiencePoint, 1000, false);
            if(player.GetPointsTotal(EExperiencePoint) <= pointsBefore)
            {
                lastMessage = "limit";
                return false;
            }
        }
        else if(action == "witcher3_skill") player.levelManager.AddPoints(ESkillPoint, 1, true);
        else return false;
        lastMessage = "applied";
        return true;
    }
}
