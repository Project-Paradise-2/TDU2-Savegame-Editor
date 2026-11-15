using TDU2SaveEditor.Core;

namespace TDU2SaveEditor.Services;

/// <summary>High-level, curated savegame mutations (mirrors the Python toolkit presets).</summary>
public static class Editing
{
    public static XObject? Obj(XValue? v) => v as XObject;
    public static XArray? Arr(XValue? v) => v as XArray;

    public static long GetLong(XObject? o, string key, long def = 0)
        => o?[key] is XPrim p ? p.AsLong() : def;

    public static ulong GetULong(XObject? o, string key, ulong def = 0)
        => o?[key] is XPrim p ? p.AsULong() : def;

    public static string GetString(XObject? o, string key, string def = "")
        => (o?[key] as XPrim)?.AsString() ?? def;

    public static bool GetBool(XObject? o, string key, bool def = false)
        => o?[key] is XPrim p && p.Value is bool b ? b : def;

    public static void SetValue(XObject o, string key, XValue value) => o[key] = value;

    public static void SetLong(XObject o, string key, long value)
    {
        if (o[key] is XPrim p) p.Value = value;
        else o[key] = new XPrim(XType.SInt32, value);
    }

    public static void SetULong(XObject o, string key, ulong value)
    {
        if (o[key] is XPrim p) p.Value = value;
        else o[key] = new XPrim(XType.UInt64, value);
    }

    public static void SetBool(XObject o, string key, bool value)
    {
        if (o[key] is XPrim p) p.Value = value;
        else o[key] = new XPrim(XType.Bool, value);
    }

    public static void SetFloat(XObject o, string key, float value)
    {
        if (o[key] is XPrim p) p.Value = value;
        else o[key] = new XPrim(XType.Float, value);
    }

    // ------------------------------------------------------------------
    // Profile / quick presets
    // ------------------------------------------------------------------

    public static void SetMoney(XObject dataRoot, long money)
    {
        if (dataRoot["Driver"] is XObject d) SetLong(d, "Money", money);
    }

    public static void SetCoupons(XObject dataRoot, long coupons)
    {
        if (dataRoot["Driver"] is XObject d) SetLong(d, "NbCoupon", coupons);
    }

    public static void MaxLevels(XObject dataRoot)
    {
        var lvl = dataRoot["PlayerLevels"] as XObject;
        if (lvl != null)
        {
            SetLong(lvl, "RacingLevel", 10000);
            SetLong(lvl, "CollectionLevel", 10000);
            SetLong(lvl, "SocialLevel", 10000);
            if (lvl["CruisingLevels"] is XObject areas)
            {
                SetLong(areas, "Area_0", 2080);
                SetLong(areas, "Area_1", 2080);
                SetLong(areas, "Area_2", 2425);
                SetLong(areas, "Area_3", 2640);
                SetLong(areas, "Area_4", 2550);
                SetLong(areas, "Area_5", 2725);
            }
            if (lvl["HaircutsBought"] is XArray hc)
            {
                for (int i = 0; i < hc.Items.Count; i++)
                    if (hc.Items[i] is XPrim p) p.Value = 10L;
            }
            if (lvl["AreaTotalRoads"] is XArray total && lvl["AreaRoadsUnlocked"] is XArray unlocked)
            {
                unlocked.Items.Clear();
                foreach (var it in total.Items)
                    unlocked.Items.Add(new XPrim(it.Type, ((XPrim)it).Value));
            }
            SetBool(lvl, "AirportUnlocked", true);
            SetBool(lvl, "NewsAirportSend", true);
        }

        UnlockLicenses(dataRoot);
        MaxContests(dataRoot);
        UnlockEvents(dataRoot);
        UnlockWrecks(dataRoot);
        UnlockPhotos(dataRoot);
        MaxChase(dataRoot);
        MaxStats(dataRoot);
        MaxShops(dataRoot);
    }

    public static void UnlockLicenses(XObject dataRoot)
    {
        if (dataRoot["PlayerLicenses"] is not XObject lic) return;

        SetLong(lic, "CurLicenseA", 3);
        SetLong(lic, "CurLicenseB", 2);
        SetLong(lic, "CurLicenseC", 2);

        if (lic["Licenses"] is XArray licenses)
        {
            foreach (var l in licenses.Items.OfType<XObject>())
            {
                SetBool(l, "Passed", true);
                SetBool(l, "Looked", true);
                SetLong(l, "RemainingReward", 0);
                SetLong(l, "PhotoIndex", 1);
                if (l["DrivingTest"] is XArray tests)
                    foreach (var t in tests.Items.OfType<XObject>())
                    {
                        SetBool(t, "Success", true);
                        SetBool(t, "Try", true);
                        SetLong(t, "Score", 50000);
                    }
            }
        }
    }

    public static void MaxContests(XObject dataRoot)
    {
        if (dataRoot["PlayerLicenses"] is not XObject lic) return;
        if (lic["Contests"] is not XArray contests) return;
        foreach (var c in contests.Items.OfType<XObject>())
        {
            SetBool(c, "Passed", true);
            SetBool(c, "Looked", true);
            SetLong(c, "RemainingReward", 0);
            SetLong(c, "Cup", 3);
            SetLong(c, "State", 3);
            if (c["Challenge"] is XArray ch)
                foreach (var x in ch.Items.OfType<XObject>())
                {
                    SetBool(x, "Success", true);
                    SetBool(x, "Try", true);
                    SetLong(x, "Score", 50000);
                }
        }
    }

    public static void UnlockEvents(XObject dataRoot)
    {
        if (dataRoot["EventsSave"] is not XObject ev) return;
        foreach (var key in new[] { "Events", "DLCEvents" })
            if (ev[key] is XArray arr)
                foreach (var e in arr.Items.OfType<XObject>())
                {
                    SetLong(e, "State", 5);
                    SetFloat(e, "TimeBeforeRespawn", 0f);
                }
    }

    public static void UnlockWrecks(XObject dataRoot)
    {
        if (dataRoot["Wrecks"] is not XObject wr) return;
        foreach (var key in new[] { "Wrecks_IBIZA", "Wrecks_HAWAI", "Wrecks_CASINO" })
            if (wr[key] is XArray arr)
                foreach (var w in arr.Items.OfType<XObject>())
                    SetBool(w, "Taken", true);
    }

    public static void UnlockPhotos(XObject dataRoot)
    {
        if (dataRoot["PhotosGP"] is not XObject ph) return;
        SetBool(ph, "Active", true);
        if (ph["GotIslandReward"] is XArray r)
            for (int i = 0; i < r.Items.Count; i++)
                if (r.Items[i] is XPrim p) p.Value = true;
        if (ph["PhotosGP"] is XArray arr)
            foreach (var p in arr.Items.OfType<XObject>())
                SetBool(p, "Taken", true);
    }

    public static void UnlockRoads(XObject dataRoot)
    {
        if (dataRoot["PlayerLevels"] is not XObject lvl) return;

        long[] maxRoads = { 467, 924, 309, 1144, 426, 998 };
        long[] maxCruising = { 2080, 2080, 2425, 2640, 2550, 2725 };

        if (lvl["AreaTotalRoads"] is XArray total) FillArray(total, maxRoads);
        if (lvl["AreaRoadsUnlocked"] is XArray unlocked) FillArray(unlocked, maxRoads);

        if (lvl["CruisingLevels"] is XObject areas)
            for (int i = 0; i < maxCruising.Length; i++)
                SetLong(areas, $"Area_{i}", maxCruising[i]);

        // The actual road-discovery bitmask: every one of the 8700 64-bit
        // masks fully discovered. Without this the game recomputes the
        // summary counts and the roads stay undiscovered.
        if (dataRoot["RoadLock"] is XObject rl && rl["Locks"] is XArray locks)
            FillUniform(locks, ulong.MaxValue);
    }

    public static void MaxCasino(XObject dataRoot)
    {
        if (dataRoot["Casino"] is not XObject casino) return;

        SetBool(casino, "_hasSeenCasinoProgressionTutorial", true);
        SetBool(casino, "_hasSeenCasinoVIPStatusCinematic", true);
        SetBool(casino, "_hasSeenCasinoACEStatusCinematic", true);
        SetBool(casino, "_has_seen_vip_intro_cine", true);
        if (casino["unlockables0"] is XPrim up)
            up.Value = up.AsULong() | (1UL << 54);

        if (casino["CasinoProgressionSave"] is not XObject prog) return;

        SetBool(prog, "_vip_status", true);
        SetLong(prog, "_vip_terrace_seen", 10);
        SetLong(prog, "_cocktail_unlock_flag", 32767);
        SetULong(prog, "_emote_unlock_flag", 4294967295);
        SetLong(prog, "_cloth_bought", 33554431);
        SetLong(prog, "_shoes_bought", 63);
        SetLong(prog, "_glasses_bought", 15);
        SetLong(prog, "_time_attack_best_score", 13443);
        SetLong(prog, "_time_attack_vip_best_score", 14014);
        SetLong(prog, "_time_attack_played_count", 10);
        SetLong(prog, "_satellite_prize_inspected_flag", 7);
        SetBool(prog, "_hall_car_seen", true);
        SetBool(prog, "_hall_car_won", true);
        SetBool(prog, "_vip_car_seen", true);
        SetBool(prog, "_vip_car_won", true);
        SetBool(prog, "_cloth_chic_set", true);
        SetBool(prog, "_furniture_chic_set", true);
        SetBool(prog, "_sticker_set", true);
        SetBool(prog, "_roulette_used_historic", true);
        SetBool(prog, "_roulette_used_rebet", true);
        FillArrayConst(prog, "_roulette_result_seen_array", 1);
        SetLong(prog, "_green_seen_count", 5);
        FillArrayConst(prog, "_cocktail_drink_array", 5);
        FillArrayConst(prog, "_cocktail_offered_array", 5);
        FillArrayConst(prog, "_cocktail_received_array", 10);
        FillArrayConst(prog, "_cocktail_different_players_offered_to", 1);
        FillArrayConst(prog, "_emote_played_array", 1);
        SetLong(prog, "_cashier_seen_with_success_count", 10);
        SetBool(prog, "_rankings_seen", true);
        SetBool(prog, "_look_at_mylife_done", true);
        SetBool(prog, "_photo_mode_used", true);
        SetLong(prog, "_cocktail_drink_count_on_poker", 25);
        SetLong(prog, "_friend_count", 5);
        SetLong(prog, "_session_invite_friend_count", 10);
        SetBool(prog, "_look_at_mylife_of_other_done", true);
        SetLong(prog, "_sitandgo_created_private_count", 5);
        SetLong(prog, "_sitandgo_joined_private_count", 5);
        SetLong(prog, "_sitandgo_played_to_the_end", 5);
        FillArrayConst(prog, "_cash_game_unique_opponents_array", 1);
        SetLong(prog, "_cash_game_money_taken_from_opponent", 2500);

        foreach (var e in Elements(prog, "_array_save_slotmachines"))
        {
            SetLong(e, "_combination_count", 32);
            SetLong(e, "_jackpot_count", 1);
            SetLong(e, "_jackpot_won_bitfield", 63);
            SetLong(e, "_money_won", 10000);
            SetLong(e, "_play_count", 10);
            SetLong(e, "_played_with_2_lines", 10);
            SetLong(e, "_played_with_3_lines", 20);
            SetLong(e, "_played_with_5_lines", 30);
            SetLong(e, "_played_with_9_lines", 40);
            SetLong(e, "_play_by_relaunch_count", 25);
            FillArrayConst(e, "_p_won_combination_array", 1);
            FillArrayConst(e, "_p_played_combination_array", 1);
        }

        foreach (var e in Elements(prog, "_array_save_roulettes"))
        {
            SetLong(e, "_combination_count", 16);
            SetLong(e, "_best_win_in_a_row", 5);
            FillArrayConst(e, "_p_won_combination_array", 1);
            FillArrayConst(e, "_p_played_combination_array", 1);
            SetLong(e, "_green_won_count", 2);
            SetLong(e, "_green_seen_count", 2);
            SetLong(e, "_money_won", 10000);
            SetLong(e, "_play_count", 10);
        }

        foreach (var e in Elements(prog, "_array_save_poker_cashgame"))
        {
            SetLong(e, "_combination_count", 10);
            SetLong(e, "_win_count", 50);
            SetLong(e, "_best_win_in_a_row", 5);
            FillArrayConst(e, "_p_won_combination_array", 1);
            FillArrayConst(e, "_p_played_combination_array", 1);
            SetLong(e, "_money_won", 10000);
            SetLong(e, "_play_count", 10);
        }

        foreach (var e in Elements(prog, "_array_save_poker_sitandgo_public"))
        {
            SetLong(e, "_combination_count", 10);
            FillArrayConst(e, "_ranks_count", 1);
            FillArrayConst(e, "_p_won_combination_array", 1);
            FillArrayConst(e, "_p_played_combination_array", 1);
            SetLong(e, "_win_before_flop_count", 20);
            SetLong(e, "_win_before_turn_count", 10);
            SetLong(e, "_win_before_river_count", 10);
            SetLong(e, "_win_count", 75);
            SetLong(e, "_best_win_in_a_row", 5);
            SetLong(e, "_money_won", 50000);
            SetLong(e, "_play_count", 10);
        }

        foreach (var e in Elements(prog, "_array_save_poker_sitandgo_private"))
        {
            SetLong(e, "_combination_count", 10);
            FillArrayConst(e, "_ranks_count", 1);
            FillArrayConst(e, "_p_won_combination_array", 1);
            FillArrayConst(e, "_p_played_combination_array", 1);
            SetLong(e, "_play_count", 10);
        }

        foreach (var e in Elements(prog, "_array_save_poker_satellite"))
        {
            SetLong(e, "_combination_count", 10);
            FillArrayConst(e, "_ranks_count", 1);
            FillArrayConst(e, "_p_won_combination_array", 1);
            FillArrayConst(e, "_p_played_combination_array", 1);
            SetLong(e, "_win_count", 2);
            SetLong(e, "_money_won", 100000);
            SetLong(e, "_play_count", 3);
        }
    }

    private static IEnumerable<XObject> Elements(XObject o, string key)
    {
        if (o[key] is XArray a)
            foreach (var it in a.Items)
                if (it is XObject eo) yield return eo;
    }

    private static void FillArrayConst(XObject o, string key, long value)
    {
        if (o[key] is XArray a)
            FillArrayConst(a, value);
    }

    private static void FillArrayConst(XArray a, long value)
    {
        int n = a.Items.Count;
        a.Items.Clear();
        for (int i = 0; i < n; i++)
            a.Items.Add(new XPrim(a.ElementType, value));
    }

    private static void FillArray(XArray arr, params long[] values)
    {
        arr.Items.Clear();
        foreach (var v in values)
            arr.Items.Add(new XPrim(arr.ElementType, v));
    }

    private static void FillUniform(XArray arr, ulong value)
    {
        int n = arr.Items.Count;
        arr.Items.Clear();
        for (int i = 0; i < n; i++)
            arr.Items.Add(new XPrim(arr.ElementType, value));
    }

    private static void MaxChase(XObject dataRoot)
    {
        if (dataRoot["Chase"] is not XObject chase) return;
        SetLong(chase, "OutlawVictories", 10);
        SetLong(chase, "OutlawMaxVictories", 10);
        SetLong(chase, "PoliceVictories", 10);
        SetLong(chase, "Rewards", 10);
        SetBool(chase, "ResetNeeded", false);
    }

    private static void MaxShops(XObject dataRoot)
    {
        if (dataRoot["SCPhoneCalls"] is XObject sc)
        {
            foreach (var k in new[]
            {
                "BeenInClothesShop", "BeenInStickerShop", "BeenInHairShop", "BeenInSurgeryShop",
                "BeenInCarWash", "BeenInPhotograph", "BeenInRSDirtHawai", "BeenInRSClassicHawai"
            })
                SetBool(sc, k, true);
        }
        if (dataRoot["Driver"] is XObject drv)
        {
            SetLong(drv, "NbSurgeries", 10);
            SetLong(drv, "NbHaircuts", 40);
            SetBool(drv, "HasBeenMillionaire", true);
            SetLong(drv, "LastSeenClubRank", 3);
        }
    }

    private static void MaxStats(XObject dataRoot)
    {
        if (dataRoot["StatisticsData"] is not XObject stats) return;
        var updates = new (string key, double val)[]
        {
            ("SoloWon",100),("ICEvsBotWon",50),("ICEvsHumanWon",50),
            ("MultiRaceWon",50),("MultiRankedWon",50),("MultiNonRankedWon",50),
            ("MultiSpeedWon",50),("MultiTrapWon",50),("MultiOrientationWon",50),("MultiEliminatorWon",50),
            ("NbCarUpgrade",100),("NbBikeUpgrade",50),("NbCarPaint",50),("NbBikePaint",50),
            ("NbHouseBought",25),("NbClothesBought",150),("NbClothesChange",50),("NbWatchesBought",20),
            ("NbHaircutsBought",40),("NbCarWithStickersBought",10),("NbCarSticked",20),("NbStickersBought",50),
            ("NbFriendsInvited",20),("NbFriendsJoined",20),("NbClubMaker",1),("NbClubJoiner",1),
            ("ClubWon",50),("InterClubWonByTeam",50),("OdometerInnerClub",100.0),
            ("CrcChaWon",50),("CrcChaRuns",50),("ExtraChaWon",50),
            ("ChaseHunterWon",20),("ChasePreyWon",20),("TimeInCoop",3600.0),
            ("KYDWon",20),("FTLWon",20),("NbCodrivingAsPilot",20),("NbCodrivingAsCopilot",20)
        };
        foreach (var (key, val) in updates)
            if (stats[key] is XPrim p)
            {
                if (p.Type == XType.Float) p.Value = (float)val;
                else if (p.Type == XType.Double) p.Value = val;
                else if (p.Type is XType.SInt8 or XType.SInt16 or XType.SInt32 or XType.SInt64) p.Value = (long)val;
                else if (p.Type is XType.UInt8 or XType.UInt16 or XType.UInt32 or XType.UInt64) p.Value = (ulong)val;
            }
    }

    public static void CleanCars(XObject dataRoot)
    {
        if (dataRoot["Garage"] is not XArray garage) return;
        foreach (var entry in garage.Items.OfType<XObject>())
        {
            if (entry["Car"] is not XObject car) continue;
            if (car["Car Dirt"] is not XObject dirt) continue;
            if (dirt["Dirt Data"] is XArray data)
                for (int i = 0; i < data.Items.Count; i++)
                    if (data.Items[i] is XPrim p) p.Value = (p.Type == XType.Float) ? 0f : (p.Type == XType.Double ? 0.0 : (object)0L);
            SetFloat(dirt, "CarWashCoating", 0f);
            foreach (var k in new[] { "BodyDirt", "GlassDirt", "RimsDirt", "BodyScratches" })
                if (dirt[k] is XPrim p2) p2.Value = p2.Type == XType.Double ? 0.0 : (object)0f;
        }
    }

    // ------------------------------------------------------------------
    // Garage operations
    // ------------------------------------------------------------------

    public static void TuneCar(XObject car, int acc, int top, int brk)
    {
        if (car["Car Upgrades"] is XObject up)
        {
            SetLong(up, "Acceleration", acc);
            SetLong(up, "Top Speed", top);
            SetLong(up, "Braking", brk);
        }
    }

    public static void SwapArchetype(XObject car, int archetype)
    {
        SetLong(car, "Archetype", archetype);
    }

    /// <summary>
    /// Frees a garage slot by copying the exact "empty car" template (taken from an
    /// existing empty slot in the same garage) into the target entry. This keeps the
    /// garage array size and all bay indices intact, which is what the game expects.
    /// Returns false if the slot does not exist.
    /// </summary>
    public static bool DeleteCar(XObject dataRoot, XArray garage, int index)
    {
        if (index < 0 || index >= garage.Items.Count) return false;
        if (garage.Items[index] is not XObject entry || entry["Car"] is not XObject car) return false;

        XObject? template = FindEmptyCarTemplate(garage, index);
        if (template != null)
            CopyInto(template, car);
        else
            ResetCarToEmpty(car);

        if (dataRoot["Driver"] is XObject driver && GetLong(driver, "CurrentCar") == index)
            SetLong(driver, "CurrentCar", 0);

        return true;
    }

    private static XObject? FindEmptyCarTemplate(XArray garage, int excludeIndex)
    {
        for (int i = 0; i < garage.Items.Count; i++)
        {
            if (i == excludeIndex) continue;
            if (garage.Items[i] is not XObject e || e["Car"] is not XObject c) continue;
            if (GetULong(c, "ID") == 0xFFFFFFFFUL)
                return c;
        }
        return null;
    }

    private static void ResetCarToEmpty(XObject car)
    {
        SetULong(car, "ID", 0xFFFFFFFFUL);
        SetLong(car, "Status", 2);
        SetLong(car, "Archetype", 0);
        SetULong(car, "VIN", 0);
        SetFloat(car, "UpgradeRate", 0f);
        SetLong(car, "PaintType", 0);
        SetLong(car, "PaintIndice", 255);
        SetLong(car, "RimIndice", 255);
        SetLong(car, "InteriorIndice", 255);
        foreach (var k in new[]
        {
            "DeluxeMainColorR", "DeluxeMainColorG", "DeluxeMainColorB", "DeluxeMainColorA",
            "DeluxeSecColorR", "DeluxeSecColorG", "DeluxeSecColorB", "DeluxeSecColorA"
        })
            SetLong(car, k, 0);
        foreach (var k in new[]
        {
            "DeluxeLogoColorR", "DeluxeLogoColorG", "DeluxeLogoColorB", "DeluxeLogoColorA",
            "DeluxeSec2ColorR", "DeluxeSec2ColorG", "DeluxeSec2ColorB", "DeluxeSec2ColorA"
        })
            SetLong(car, k, 255);
        SetLong(car, "Price", 0);
        SetFloat(car, "OdometerKms", 0f);
        SetFloat(car, "PlayTime", 0f);
        SetBool(car, "HasSticker", false);
        if (car["Car Upgrades"] is XObject up)
        {
            SetLong(up, "Acceleration", 0);
            SetLong(up, "Top Speed", 0);
            SetLong(up, "Braking", 0);
        }
        if (car["Car Dirt"] is XObject dirt)
        {
            if (dirt["Dirt Data"] is XArray data)
                foreach (var v in data.Items)
                    if (v is XPrim p) p.Value = p.Type == XType.Float ? 0f : (p.Type == XType.Double ? 0.0 : (object)0L);
            SetFloat(dirt, "CarWashCoating", 0f);
            foreach (var k in new[] { "BodyDirt", "GlassDirt", "RimsDirt", "BodyScratches" })
                if (dirt[k] is XPrim p2) p2.Value = p2.Type == XType.Double ? 0.0 : (object)0f;
        }
    }

    private static void CopyInto(XValue src, XValue dst)
    {
        switch (src, dst)
        {
            case (XPrim sp, XPrim dp):
                dp.Value = sp.Value;
                break;
            case (XObject so, XObject dobj):
                foreach (var kv in so.Items)
                    if (dobj[kv.Key] is XValue dv) CopyInto(kv.Value, dv);
                break;
            case (XArray sa, XArray da):
                for (int i = 0; i < sa.Count && i < da.Count; i++)
                    CopyInto(sa.Items[i], da.Items[i]);
                break;
        }
    }
}
