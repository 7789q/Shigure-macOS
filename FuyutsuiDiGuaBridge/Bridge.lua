local _, addonTable = ...

if Fuyutsui and Fuyutsui.InitializeDiGuaBridge then
    Fuyutsui:InitializeDiGuaBridge()
    addonTable.supportedDiGuaVersion = Fuyutsui.DiGuaBridge.supportedVersion
    addonTable.castSpellByIcon = Fuyutsui.DiGuaBridge.castSpellByIcon

    -- Mirror DiGua's own UNIT_SPELLCAST_START path. This frame is deliberately
    -- hosted by the bridge addon so the live monitor uses the same event source
    -- as the working DiGua warning code, rather than waiting for Shigure's
    -- timeline callback or a log file flush.
    if CreateFrame and not addonTable.shigureCastMonitor then
        local function IsAbsorbCastUnit(unit)
            if type(unit) ~= "string" or not unit:find("nameplate", 1, true) then return false end
            if not UnitCanAttack or not UnitCanAttack("player", unit) then return false end
            if select(8, GetInstanceInfo()) ~= 2993 then return false end
            local mapID = C_Map and C_Map.GetBestMapForUnit and C_Map.GetBestMapForUnit("player") or 0
            if mapID ~= 2588 and mapID ~= 2590 then return false end
            if IsIndoors and IsIndoors() == true then return false end
            if UnitLevel(unit) ~= UnitLevel("player") + 1 then return false end
            if UnitPowerType(unit) ~= 1 then return false end
            if UnitClassification(unit) ~= "elite" then return false end
            if UnitAffectingCombat(unit) ~= true then return false end
            if select(2, UnitCreatureFamily(unit)) then return false end
            if UnitSpellTargetName(unit) then return false end
            return true
        end

        local frame = CreateFrame("Frame")
        frame:RegisterEvent("UNIT_SPELLCAST_START")
        frame:RegisterEvent("UNIT_SPELLCAST_STOP")
        frame:RegisterEvent("UNIT_SPELLCAST_INTERRUPTED")
        frame:RegisterEvent("UNIT_SPELLCAST_FAILED")
        frame:RegisterEvent("UNIT_SPELLCAST_FAILED_QUIET")
        frame:RegisterEvent("UNIT_SPELLCAST_SUCCEEDED")
        frame:SetScript("OnEvent", function(_, event, unit, castGUID, spellID)
            if not IsAbsorbCastUnit(unit) or not Fuyutsui then return end
            if event == "UNIT_SPELLCAST_START" then
                if Fuyutsui.ObserveAOEEnemyCast then
                    Fuyutsui:ObserveAOEEnemyCast(unit, castGUID, spellID, false)
                end
            elseif Fuyutsui.FinishAOEEnemyCast then
                local reason = event == "UNIT_SPELLCAST_SUCCEEDED" and "succeeded"
                    or event == "UNIT_SPELLCAST_INTERRUPTED" and "interrupted"
                    or event == "UNIT_SPELLCAST_FAILED" and "failed"
                    or event == "UNIT_SPELLCAST_FAILED_QUIET" and "failed_quiet"
                    or "stopped"
                Fuyutsui:FinishAOEEnemyCast(unit, castGUID, spellID, reason)
            end
        end)
        addonTable.shigureCastMonitor = frame
    end

    -- DiGua's current live source creates the absorb countdown from
    -- NamePlateEnterCombat.lua: it checks NAME_PLATE_UNIT_ADDED, waits until
    -- the unit is in combat, then calls CustomEncounterBar(132334, 11.7,...).
    -- The addon table passed to this compatibility addon is *not* DiGua's
    -- table, so hooking addonTable.CustomEncounterBar can never work here.
    -- Mirror the source conditions directly and feed the same real-time bar
    -- into Shigure; this does not depend on the timeline payload or combat log.
    if CreateFrame and not addonTable.shigureAbsorbBarMonitor then
        local triggered = {}
        local tickers = {}

        local function IsAbsorbWarningUnit(unit)
            if type(unit) ~= "string" or not unit:find("nameplate", 1, true) then return false end
            if not UnitCanAttack or not UnitCanAttack("player", unit) then return false end
            if select(8, GetInstanceInfo()) ~= 2993 then return false end
            local mapID = C_Map and C_Map.GetBestMapForUnit and C_Map.GetBestMapForUnit("player") or 0
            if mapID ~= 2588 and mapID ~= 2590 then return false end
            if IsIndoors and IsIndoors() == true then return false end
            if UnitLevel(unit) ~= UnitLevel("player") + 1 then return false end
            if UnitPowerType(unit) ~= 1 then return false end
            if UnitClassification(unit) ~= "elite" then return false end
            if UnitAffectingCombat(unit) ~= true then return false end
            if select(2, UnitCreatureFamily(unit)) then return false end
            return true
        end

        local function CancelTicker(unit)
            local ticker = tickers[unit]
            if ticker then ticker:Cancel() end
            tickers[unit] = nil
        end

        local function CheckUnit(unit)
            if not unit or triggered[unit] then return true end
            local matched = IsAbsorbWarningUnit(unit)
            if matched then
                triggered[unit] = true
                CancelTicker(unit)
                if Fuyutsui.ObserveAOEDiGuaBar then
                    Fuyutsui:ObserveAOEDiGuaBar(132334, 11.7, "准备吸奶盾", unit)
                end
                return true
            end
            return false
        end

        local function WatchUnit(unit, source)
            if type(unit) ~= "string" or not unit:find("nameplate", 1, true) then return end
            if source ~= "NAME_PLATE_UNIT_ADDED" and UnitExists and not UnitExists(unit) then return end
            CancelTicker(unit)
            if CheckUnit(unit) or not C_Timer or not C_Timer.NewTicker then return end
            local ticker
            ticker = C_Timer.NewTicker(1, function()
                if CheckUnit(unit) or not UnitExists(unit) then
                    ticker:Cancel()
                    tickers[unit] = nil
                end
            end)
            tickers[unit] = ticker
        end

        local frame = CreateFrame("Frame")
        frame:RegisterEvent("NAME_PLATE_UNIT_ADDED")
        frame:RegisterEvent("NAME_PLATE_UNIT_REMOVED")
        frame:RegisterEvent("PLAYER_ENTERING_WORLD")
        frame:RegisterEvent("PLAYER_REGEN_DISABLED")
        frame:SetScript("OnEvent", function(_, event, unit)
            if event == "NAME_PLATE_UNIT_REMOVED" then
                if unit then
                    CancelTicker(unit)
                    triggered[unit] = nil
                    if Fuyutsui.CancelAOEDiGuaBar then
                        Fuyutsui:CancelAOEDiGuaBar(unit)
                    end
                end
                return
            end
            if event == "NAME_PLATE_UNIT_ADDED" then
                WatchUnit(unit, event)
                return
            end
            for index = 1, 40 do
                WatchUnit("nameplate" .. index, event)
            end
        end)
        addonTable.shigureAbsorbBarMonitor = frame
    end
end
