local addon, ns = ...

local isSec = issecretvalue
local warning = {
    events = {},
    eventStates = {},
    castOwners = {},
    completedCasts = {},
    pendingTerminals = {},
    pendingCasts = {},
    polledCasts = {},
    castPollElapsed = 0,
    nextSequence = 0,
    nextProtectedCastSequence = 0,
    initialized = false,
}

Fuyutsui.AOEWarningConfig = Fuyutsui.AOEWarningConfig or {
    prepareLeadSeconds = 12,
    impactActiveSeconds = 10,
    absorbPrepareSeconds = 23,
    absorbDiGuaCastSeconds = 11.7,
    absorbVirtueDelaySeconds = 2,
    absorbInferenceLeadSeconds = 0.25,
    absorbTimelineFallbackGraceSeconds = 15,
    virtueWindowSeconds = 1,
    inputMarginSeconds = 0.35,
    defaultGCDSeconds = 1.5,
    castTerminalGraceSeconds = 0.5,
    protectedCorrelationSeconds = 0.5,
    allowUnverifiedChannels = false,
}

local function IsSecret(value)
    return isSec and isSec(value)
end

local function SafeNumber(value)
    return not IsSecret(value) and type(value) == "number" and value or nil
end

local function SafeString(value)
    return not IsSecret(value) and type(value) == "string" and value or nil
end

local diagnosticCounters = {
    bridgeRequest = { state = "aoeBridgeRequests", field = "AOE桥接请求数" },
    bridgeSuccess = { state = "aoeBridgeSuccesses", field = "AOE桥接成功数" },
    castAwareWarning = { state = "aoeCastAwareWarnings", field = "AOE带技能预警数" },
    rawCast = { state = "aoeRawCasts", field = "AOE原始读条数" },
    protectedSpell = { state = "aoeProtectedSpells", field = "AOE技能受保护数" },
    protectedHostility = { state = "aoeProtectedHostility", field = "AOE敌对状态受保护数" },
    protectedMatched = { state = "aoeProtectedMatches", field = "AOE受保护匹配数" },
    enemyCast = { state = "aoeEnemyCasts", field = "AOE敌方读条数" },
    castRejected = { state = "aoeCastsRejected", field = "AOE读条未采纳数" },
    castMatched = { state = "aoeCastsMatched", field = "AOE读条匹配数" },
    castUnmatched = { state = "aoeCastsUnmatched", field = "AOE读条未匹配数" },
    castSucceeded = { state = "aoeCastsSucceeded", field = "AOE读条成功数" },
    castFailed = { state = "aoeCastsFailed", field = "AOE读条失败数" },
}

local expectedSpellFields = {
    "AOE预警技能低位",
    "AOE预警技能中位",
    "AOE预警技能高位",
}
local observedSpellFields = {
    "AOE读条技能低位",
    "AOE读条技能中位",
    "AOE读条技能高位",
}

local function PublishSpellID(stateKey, fields, spellID)
    spellID = SafeNumber(spellID)
    if not spellID or spellID < 0 or spellID > 16777215 then return end
    Fuyutsui.state[stateKey] = math.floor(spellID)
    for i = 1, #fields do
        Fuyutsui:UpdateStateBlock("状态", fields[i])
    end
end

function Fuyutsui:PublishAOEDiagnostic(counterName, expectedSpellID, observedSpellID)
    local counter = diagnosticCounters[counterName]
    if not counter then return end
    self.state[counter.state] = ((self.state[counter.state] or 0) + 1) % 256
    self:UpdateStateBlock("状态", counter.field)
    PublishSpellID("aoeExpectedSpellID", expectedSpellFields, expectedSpellID)
    PublishSpellID("aoeObservedSpellID", observedSpellFields, observedSpellID)
end

function Fuyutsui:PublishAOEDiagnosticState()
    for _, counter in pairs(diagnosticCounters) do
        self:UpdateStateBlock("状态", counter.field)
    end
    for i = 1, #expectedSpellFields do
        self:UpdateStateBlock("状态", expectedSpellFields[i])
    end
    for i = 1, #observedSpellFields do
        self:UpdateStateBlock("状态", observedSpellFields[i])
    end
end

local function DebugEnabled()
    local char = Fuyutsui.db and Fuyutsui.db.char
    return char and char.aoeWarningDebug == true
end

local function DebugLog(message, ...)
    if not DebugEnabled() then return end
    local ok, text = pcall(string.format, message, ...)
    print("|cff00ff00[Fuyutsui AOE]|r " .. (ok and text or message))
end

-- 默认不向 WoW 聊天框刷屏；需要排查时通过 /fu aoedebug 显式开启。
local function TraceLog(message, ...)
    DebugLog(message, ...)
end

local function SoundEventType(path)
    path = SafeString(path)
    if not path then return nil end
    local normalized = path:gsub("\\", "/"):lower()
    if normalized:find("zhunbeixinaidun", 1, true) then return 2 end
    if normalized:find("aoe", 1, true) then return 1 end
    return nil
end

local function EventNameType(name)
    name = SafeString(name)
    if name == "准备AOE" then return 1 end
    if name == "准备吸奶盾" then return 2 end
    return nil
end

local function EventKey(runtimeID)
    return "timeline:" .. tostring(runtimeID)
end

local function CastKey(castGUID, unitGUID, spellID, startedAt)
    local safeCastGUID = SafeString(castGUID)
    if safeCastGUID then return "cast:" .. safeCastGUID end
    if unitGUID and spellID and startedAt then
        return table.concat({ "unit", unitGUID, tostring(spellID), tostring(startedAt) }, ":")
    end
    return nil
end

local function ReleaseCast(event, completed)
    local cast = event and event.cast
    if not cast then return end
    if cast.key then
        warning.pendingTerminals[cast.key] = nil
        warning.castOwners[cast.key] = nil
        if completed then warning.completedCasts[cast.key] = true end
    end
    event.cast = nil
end

local function RemoveEvent(id, reason)
    local event = warning.events[id]
    if not event then return end
    TraceLog(
        "事件结束 event=%s type=%d status=%s outcome=%s reason=%s",
        tostring(event.runtimeID or event.id),
        event.eventType,
        tostring(event.status or "unknown"),
        tostring(event.castOutcome or "none"),
        tostring(reason or "unknown"))
    ReleaseCast(event, false)
    warning.events[id] = nil
    if event.runtimeID then warning.eventStates[event.runtimeID] = nil end
    DebugLog(
        "结束 reason=%s type=%d event=%s spell=%s",
        reason or "unknown",
        event.eventType,
        tostring(event.runtimeID or event.id),
        tostring(event.spellID or 0))
end

local function NewEvent(id, eventType, impactAt, source, options)
    if not eventType or not impactAt then return nil end
    options = options or {}
    if warning.events[id] then RemoveEvent(id, "event-id-reused") end

    local spellID = SafeNumber(options.spellID)
    if spellID and spellID <= 0 then spellID = nil end
    warning.nextSequence = warning.nextSequence + 1
    local event = {
        id = id,
        runtimeID = SafeNumber(options.runtimeID),
        eventType = eventType,
        impactAt = impactAt,
        source = source,
        impactAnchor = options.impactAnchor
            or (source == "diguabar" and "diguabar" or nil),
        spellID = spellID,
        reservationOnly = spellID == nil,
        createdAt = GetTime(),
        prepareLeadSeconds = SafeNumber(options.prepareLeadSeconds),
        expiresAt = impactAt + Fuyutsui.AOEWarningConfig.impactActiveSeconds,
        sequence = warning.nextSequence,
        status = "reserved",
    }
    if eventType == 2 and source == "timeline" then
        -- A late protected cast may arrive after the predicted impact. Keep
        -- the reservation alive through the correlation grace period, but
        -- never turn that grace period into a successful execution signal.
        event.expiresAt = math.max(
            event.expiresAt,
            impactAt + Fuyutsui.AOEWarningConfig.absorbTimelineFallbackGraceSeconds
                + Fuyutsui.AOEWarningConfig.impactActiveSeconds)
    end
    warning.events[id] = event
    if spellID then
        Fuyutsui:PublishAOEDiagnostic("castAwareWarning", spellID)
    end
    DebugLog(
        "预警 type=%d event=%s spell=%s remaining=%.2f%s",
        eventType,
        tostring(event.runtimeID or id),
        tostring(spellID or 0),
        math.max(0, impactAt - GetTime()),
        event.reservationOnly and " 仅资源预留，不允许预铺美德" or "")
    TraceLog(
        "事件创建 event=%s type=%d expectedSpell=%s source=%s reservationOnly=%s impact=%.2f",
        tostring(event.runtimeID or id),
        eventType,
        tostring(event.spellID or 0),
        tostring(source),
        event.reservationOnly and "true" or "false",
        impactAt)
    if spellID and Fuyutsui.TryBindPendingAOECast then
        Fuyutsui:TryBindPendingAOECast(event)
    end
    return event
end

local function GetEventRemaining(eventID)
    if not eventID or not C_EncounterTimeline or not C_EncounterTimeline.GetEventTimeRemaining then
        return nil
    end
    local ok, remaining = pcall(C_EncounterTimeline.GetEventTimeRemaining, eventID)
    return ok and SafeNumber(remaining) or nil
end

local function GetSpellRemainingSeconds(spellID)
    if not C_Spell or type(C_Spell.GetSpellCooldown) ~= "function" then return nil end
    local ok, cooldown = pcall(C_Spell.GetSpellCooldown, spellID)
    if not ok or type(cooldown) ~= "table" then return nil end
    if cooldown.isEnabled == false then return nil end
    local startTime = SafeNumber(cooldown.startTime)
    local duration = SafeNumber(cooldown.duration)
    local modRate = SafeNumber(cooldown.modRate) or 1
    if not duration or duration <= 0 or modRate <= 0 then return 0 end
    if not startTime then return nil end
    return math.max(0, startTime + duration / modRate - GetTime())
end

local function GetEstimatedVirtueAt(event)
    if not event then return nil end
    if event.eventType == 2 then
        return event.virtueReadyAt
            or (event.impactAt + Fuyutsui.AOEWarningConfig.absorbVirtueDelaySeconds)
    end
    return event.cast and event.cast.endsAt or event.impactAt
end

local function DivineTollExpectedReady(event, now)
    local virtueAt = GetEstimatedVirtueAt(event)
    local cooldownRemaining = GetSpellRemainingSeconds(375576)
    if not virtueAt or not cooldownRemaining then return false end
    return cooldownRemaining <= math.max(0, virtueAt - now)
end

local function SetOutput(eventType, stage, event)
    local state = Fuyutsui.state
    if state.aoeEventType ~= eventType or state.aoeEventStage ~= stage then
        TraceLog(
            "状态转移 type=%d stage=%d event=%s spell=%s status=%s outcome=%s",
            eventType,
            stage,
            tostring(event and (event.runtimeID or event.id) or "-"),
            tostring(event and event.spellID or 0),
            tostring(event and event.status or "none"),
            tostring(event and event.castOutcome or "none"))
        state.aoeEventType = eventType
        state.aoeEventStage = stage
        Fuyutsui:UpdateStateBlock("状态", "AOE事件类型")
        Fuyutsui:UpdateStateBlock("状态", "AOE事件阶段")
    end

    local cast = event and event.cast or nil
    state.aoeProtectedCastActive = cast
        and (cast.protectedTiming == true or cast.protectedSpellID == true)
        or false
    state.aoeProtectedCastUnit = state.aoeProtectedCastActive and cast.unit or nil
    state.aoeProtectedCastIsChannel = state.aoeProtectedCastActive and cast.isChannel or false
    state.divineTollExpectedReady = DivineTollExpectedReady(event, GetTime())
    Fuyutsui:UpdateStateBlock("状态", "AOE受保护读条")
    Fuyutsui:UpdateStateBlock("状态", "AOE读条剩余")
    Fuyutsui:UpdateStateBlock("状态", "圣洁鸣钟预计可用")
end

function Fuyutsui:GetEstimatedGCDSeconds()
    if C_Spell and C_Spell.GetSpellCooldown then
        local cooldown = C_Spell.GetSpellCooldown(61304)
        local duration = type(cooldown) == "table" and SafeNumber(cooldown.duration) or nil
        if duration and duration > 0 then return duration end
    end
    local haste = GetHaste and SafeNumber(GetHaste()) or nil
    if not haste then return Fuyutsui.AOEWarningConfig.defaultGCDSeconds end
    return math.max(0.75, Fuyutsui.AOEWarningConfig.defaultGCDSeconds / (1 + haste / 100))
end

function Fuyutsui:GetGCDRemainingSeconds()
    if not C_Spell or not C_Spell.GetSpellCooldown then return 0 end
    local cooldown = C_Spell.GetSpellCooldown(61304)
    if type(cooldown) ~= "table" then return 0 end
    local startTime = SafeNumber(cooldown.startTime)
    local duration = SafeNumber(cooldown.duration)
    local modRate = SafeNumber(cooldown.modRate) or 1
    if not startTime or not duration or duration <= 0 or modRate <= 0 then return 0 end
    return math.max(0, startTime + duration / modRate - GetTime())
end

local function StageForEvent(event, now)
    local config = Fuyutsui.AOEWarningConfig
    if event.status == "succeeded" and event.completed then
        -- UNIT_SPELLCAST_SUCCEEDED fires before an absorb can be applied, but
        -- the requested post-cast delay must elapse before opening stage 3.
        -- Once that delay is over, keep stage 3 until Virtue is confirmed so a
        -- failed/queued key still has a chance to retry.
        if event.eventType == 2 then
            if not event.virtueReadyAt then return 1 end
            if not event.virtueConfirmed then
                if event.virtueReadyAt and now < event.virtueReadyAt then
                    -- Reserve the final GCD before the post-cast delay ends.
                    -- Without this guard a filler spell can start immediately
                    -- before stage 3 and push Virtue past its absorb window.
                    local reserveLead = Fuyutsui:GetEstimatedGCDSeconds()
                        + config.inputMarginSeconds
                    if event.virtueReadyAt - now <= reserveLead then return 5 end
                    return 1
                end
                return 3
            end
            -- Keep stage 3 until the event is removed after a confirmed cast
            -- and the absorb has stayed at zero for two consecutive samples.
            -- Dropping back to stage 1 here makes a successful Virtue look like
            -- a new resource reservation and can cause a healthy-group recast.
            return 3
        end
        return event.eventType == 2 and 3 or 4
    end

    local remaining = event.impactAt - now
    local prepareLead = event.prepareLeadSeconds or config.prepareLeadSeconds
    if remaining > prepareLead then return 0 end
    if event.eventType == 2 and event.source == "diguabar" and not event.cast then
        if remaining > 0 then return 1 end
        if not event.completed and event.status ~= "failed" then
            event.status = "succeeded"
            event.castOutcome = "diguabar_elapsed"
            event.completed = true
            local impactAt = event.impactAt or now
            event.virtueReadyAt = impactAt + config.absorbVirtueDelaySeconds
            event.expiresAt = event.virtueReadyAt + config.impactActiveSeconds
            TraceLog(
                "DiGua倒计时结束 event=%s，等待 %.2f 秒后进入阶段3",
                tostring(event.runtimeID or event.id),
                config.absorbVirtueDelaySeconds)
        end
        return event.virtueReadyAt and now >= event.virtueReadyAt and 3 or 1
    end
    if event.eventType == 2
        and event.source == "timeline"
        and not event.cast
        and remaining <= 0 then
        local fallbackAt = event.timelineFallbackAt
            or (event.impactAt + config.absorbTimelineFallbackGraceSeconds)
        event.timelineFallbackAt = fallbackAt
        if now < fallbackAt then
            if not event.timelineFallbackPending then
                event.timelineFallbackPending = true
                event.expiresAt = math.max(
                    event.expiresAt,
                    fallbackAt + config.impactActiveSeconds)
                DebugLog(
                    "吸奶盾时间轴到点，暂不兜底，等待迟到读条 event=%s grace=%.2f",
                    tostring(event.runtimeID or event.id),
                    math.max(0, fallbackAt - now))
            end
            return 1
        end
        if not event.timelineFallbackBlocked then
            event.timelineFallbackBlocked = true
            event.timelineFallbackPending = false
            event.status = "unknown"
            event.castOutcome = "timeline_timeout"
            TraceLog(
                "吸奶盾时间轴等待超时 event=%s expectedSpell=%s outcome=unknown，禁止进入美德窗口",
                tostring(event.runtimeID or event.id),
                tostring(event.spellID or 0))
        end
        return 1
    end
    if not event.cast or event.reservationOnly or event.cast.protectedTiming then return 1 end
    if event.eventType == 2 then
        -- Absorb warnings intentionally do not use the ordinary pre-cast
        -- stage-2 window. The cast terminal starts the post-cast delay.
        return 1
    end

    remaining = event.cast.endsAt - now
    if remaining <= 0 then return 1 end
    if event.virtueConfirmed then return 1 end
    if event.cast.executionOpened then return 2 end
    if remaining <= config.virtueWindowSeconds then
        event.cast.executionOpened = true
        DebugLog(
            "进入美德窗口 event=%s cast=%s remaining=%.2f",
            tostring(event.runtimeID or event.id),
            tostring(event.cast.castGUID),
            remaining)
        return 2
    end

    local gcd = Fuyutsui:GetEstimatedGCDSeconds()
    if remaining <= config.virtueWindowSeconds + gcd + config.inputMarginSeconds then
        if not event.cast.gcdWindowLogged then
            event.cast.gcdWindowLogged = true
            DebugLog(
                "进入最后安全GCD event=%s cast=%s remaining=%.2f",
                tostring(event.runtimeID or event.id),
                tostring(event.cast.castGUID),
                remaining)
        end
        return 5
    end
    return 1
end

local stagePriority = { [2] = 5, [3] = 4, [5] = 3, [4] = 2, [1] = 1 }

local function SelectOutput(now)
    local selected, selectedStage
    for id, event in pairs(warning.events) do
        if now > event.expiresAt then
            RemoveEvent(id, event.completed and "active-window-expired" or "未发现对应读条")
        else
            if event.cast
                and not event.completed
                and event.cast.endsAt
                and now > event.cast.endsAt + Fuyutsui.AOEWarningConfig.castTerminalGraceSeconds
                and not warning.pendingTerminals[event.cast.key] then
                DebugLog(
                    "读条结果未知，返回资源预留 event=%s cast=%s",
                    tostring(event.runtimeID or event.id),
                    tostring(event.cast.castGUID))
                TraceLog(
                    "读条终态超时 event=%s expectedSpell=%s cast=%s outcome=unknown，保留资源预留",
                    tostring(event.runtimeID or event.id),
                    tostring(event.spellID or 0),
                    tostring(event.cast.castGUID or "<protected>"))
                event.status = "unknown"
                event.timelineFallbackBlocked = true
                ReleaseCast(event, false)
                event.expiresAt = math.max(event.expiresAt, now + Fuyutsui.AOEWarningConfig.impactActiveSeconds)
            end

            local stage = StageForEvent(event, now)
            if stage > 0 and (not selected
                or stagePriority[stage] > stagePriority[selectedStage]
                or (stage == selectedStage and (event.impactAt < selected.impactAt
                    or (event.impactAt == selected.impactAt and event.sequence < selected.sequence)))) then
                selected, selectedStage = event, stage
            end
        end
    end
    return selected, selectedStage
end

function Fuyutsui:UpdateAOEWarningState()
    local event, stage = SelectOutput(GetTime())
    if not event then SetOutput(0, 0) else SetOutput(event.eventType, stage, event) end
end

local function ReadCastTiming(unit, isChannel)
    local startMS, endMS
    if isChannel then
        local _, _, _, rawStart, rawEnd = UnitChannelInfo(unit)
        startMS, endMS = SafeNumber(rawStart), SafeNumber(rawEnd)
    else
        local _, _, _, rawStart, rawEnd = UnitCastingInfo(unit)
        startMS, endMS = SafeNumber(rawStart), SafeNumber(rawEnd)
    end
    if not startMS or not endMS or endMS <= startMS then return nil end
    return startMS / 1000, endMS / 1000
end

local function FindMatchingEvent(now, spellID, endsAt)
    local selected, selectedDistance
    for _, event in pairs(warning.events) do
        if event.spellID == spellID
            and not event.reservationOnly
            and now <= event.expiresAt
            and not event.cast
            and not event.completed then
            local distance = endsAt and math.abs(event.impactAt - endsAt) or 0
            if not selected or distance < selectedDistance
                or (distance == selectedDistance and event.sequence < selected.sequence) then
                selected, selectedDistance = event, distance
            end
        end
    end
    return selected
end

local function DirectCastEventType(spellID)
    local bridge = Fuyutsui.DiGuaBridge
    local mapping = bridge and bridge.castEventTypeBySpell
    local eventType = mapping and mapping[spellID]
    if eventType == 1 or eventType == 2 then return eventType end
    return nil
end

local function FindDirectCastEvent(eventType, spellID, impactAt)
    for _, event in pairs(warning.events) do
        if event.source == "cast"
            and event.eventType == eventType
            and event.spellID == spellID
            and not event.completed
            and (not impactAt or not event.cast or not event.cast.endsAt
                or math.abs(event.cast.endsAt - impactAt)
                    <= Fuyutsui.AOEWarningConfig.protectedCorrelationSeconds) then
            return event
        end
    end
    return nil
end

local function IsNameplateUnit(unit)
    -- Match DiGua's unit-token check. Blizzard can append/encode the numeric
    -- suffix differently on protected nameplate units, so an anchored Lua
    -- pattern can silently discard an otherwise valid cast event.
    return type(unit) == "string" and unit:find("nameplate", 1, true) ~= nil
end

-- DiGua recognizes the Ritual Lord's absorb cast from the same contextual
-- filters it uses for its warning bar. In instanced combat the enemy Spell ID
-- may be protected, so this is deliberately a narrow unit/context check rather
-- than a second spell registry.
local function IsLikelyAbsorbCastUnit(unit)
    if not IsNameplateUnit(unit) then return false end
    if type(UnitCanAttack) == "function" then
        local canAttack = UnitCanAttack("player", unit)
        if IsSecret(canAttack) then
            -- The unit identity and the remaining DiGua filters are still
            -- usable when hostility is protected by instanced combat.
        elseif canAttack ~= true then
            return false
        end
    end
    if type(GetInstanceInfo) ~= "function" or select(8, GetInstanceInfo()) ~= 2993 then
        return false
    end
    local mapID = C_Map and C_Map.GetBestMapForUnit and C_Map.GetBestMapForUnit("player") or 0
    if mapID ~= 2588 and mapID ~= 2590 then return false end
    if type(IsIndoors) == "function" and IsIndoors() == true then return false end

    local unitLevel = type(UnitLevel) == "function" and UnitLevel(unit) or nil
    local playerLevel = type(UnitLevel) == "function" and UnitLevel("player") or nil
    if not IsSecret(unitLevel) and not IsSecret(playerLevel)
        and type(unitLevel) == "number" and type(playerLevel) == "number"
        and unitLevel ~= playerLevel + 1 then
        return false
    end
    local powerType = type(UnitPowerType) == "function" and UnitPowerType(unit) or nil
    if not IsSecret(powerType) and powerType ~= nil and powerType ~= 1 then return false end
    local classification = type(UnitClassification) == "function" and UnitClassification(unit) or nil
    if not IsSecret(classification) and classification ~= nil and classification ~= "elite" then
        return false
    end
    if type(UnitAffectingCombat) == "function" and UnitAffectingCombat(unit) == false then
        return false
    end
    if type(UnitCreatureFamily) == "function" then
        local family = select(2, UnitCreatureFamily(unit))
        if not IsSecret(family) and family then return false end
    end
    if type(UnitSpellTargetName) == "function" and UnitSpellTargetName(unit) then return false end
    return true
end

local function ReadProtectedCastDuration(unit, isChannel)
    local reader = isChannel and UnitChannelDuration or UnitCastingDuration
    if type(reader) ~= "function" then return nil end
    local ok, duration = pcall(reader, unit)
    if not ok or not duration then return nil end
    if type(duration) == "number" then return duration end

    -- 12.1 can protect the raw start/end milliseconds while still exposing
    -- the DurationObject used by Fuyutsui's pixel protocol. Decode its blue
    -- channel exactly as stateblocks.lua does (1.0 == 25.5 seconds).
    if type(duration.EvaluateRemainingDuration) ~= "function" then return nil end
    local curve = Fuyutsui.castCurve
    local colorOK, color = pcall(duration.EvaluateRemainingDuration, duration, curve)
    if not colorOK or not color or type(color.GetRGB) ~= "function" then return nil end
    local rgbOK, _, _, blue = pcall(color.GetRGB, color)
    if not rgbOK or type(blue) ~= "number" then return nil end
    return math.max(0, blue * 25.5)
end

local function BindEventCast(event, unit, castGUID, spellID, isChannel, timing, protectedSpell)
    local unitGUID = SafeString(UnitGUID(unit))
    warning.nextProtectedCastSequence = warning.nextProtectedCastSequence + 1
    local castKey = CastKey(castGUID, unitGUID, spellID, timing and timing.startedAt)
        or table.concat({ "unit", unit, tostring(warning.nextProtectedCastSequence) }, ":")
    if warning.castOwners[castKey] or warning.completedCasts[castKey] then return false end

    event.cast = {
        key = castKey,
        unit = unit,
        unitGUID = unitGUID,
        castGUID = SafeString(castGUID),
        spellID = spellID or event.spellID,
        startedAt = timing and timing.startedAt or nil,
        endsAt = timing and timing.endsAt or nil,
        totalSeconds = timing and (timing.endsAt - timing.startedAt) or nil,
        isChannel = isChannel == true,
        executionOpened = false,
        protectedTiming = timing == nil,
        protectedSpellID = protectedSpell == true,
    }
    event.status = "casting"
    event.castOutcome = nil
    warning.castOwners[castKey] = event.id
    if timing then
        event.impactAnchor = "actual"
        event.impactAt = timing.endsAt
        event.expiresAt = timing.endsAt + Fuyutsui.AOEWarningConfig.impactActiveSeconds
    end
    event.virtueConfirmed = false
    event.timelineFallbackBlocked = false
    event.timelineFallbackPending = false
    event.timelineFallbackAt = nil
    Fuyutsui:PublishAOEDiagnostic(
        protectedSpell and "protectedMatched" or "castMatched",
        event.spellID,
        protectedSpell and nil or spellID)
    if not timing and not protectedSpell then
        Fuyutsui:PublishAOEDiagnostic("protectedMatched", event.spellID, spellID)
    end
    DebugLog(
        protectedSpell and "受保护读条语义匹配 event=%s unit=%s castGUID=%s" or
            "匹配读条 event=%s spell=%d unit=%s unitGUID=%s castGUID=%s",
        tostring(event.runtimeID or event.id),
        protectedSpell and unit or spellID,
        protectedSpell and tostring(event.cast.castGUID) or unit,
        protectedSpell and nil or tostring(unitGUID),
        protectedSpell and nil or tostring(event.cast.castGUID))
    TraceLog(
        "读条匹配 event=%s expectedSpell=%s observedSpell=%s unit=%s cast=%s source=%s end=%s",
        tostring(event.runtimeID or event.id),
        tostring(event.spellID or 0),
        tostring(spellID or event.spellID or 0),
        tostring(unit),
        tostring(event.cast.castGUID or "<protected>"),
        protectedSpell and "受保护值" or "普通值",
        tostring(event.cast.endsAt or "未知"))
    return true
end

local function PrunePendingCasts(now)
    local threshold = Fuyutsui.AOEWarningConfig.protectedCorrelationSeconds
    for index = #warning.pendingCasts, 1, -1 do
        if now - warning.pendingCasts[index].observedAt > threshold then
            table.remove(warning.pendingCasts, index)
        end
    end
end

local function FindRecentSemanticEvent(now)
    local threshold = Fuyutsui.AOEWarningConfig.protectedCorrelationSeconds
    local selected
    for _, event in pairs(warning.events) do
        if event.spellID
            and not event.reservationOnly
            and not event.cast
            and not event.completed
            and math.abs(now - event.createdAt) <= threshold
            and (not selected or event.createdAt > selected.createdAt
                or (event.createdAt == selected.createdAt and event.sequence > selected.sequence)) then
            selected = event
        end
    end
    return selected
end

local function PublishProtectedCastDiagnostics(candidate, event)
    if candidate.diagnosticsPublished then return end
    candidate.diagnosticsPublished = true
    Fuyutsui:PublishAOEDiagnostic("rawCast", event.spellID)
    Fuyutsui:PublishAOEDiagnostic("protectedSpell", event.spellID)
end

local function BindPendingCandidate(event, candidate)
    if candidate.isChannel and not Fuyutsui.AOEWarningConfig.allowUnverifiedChannels then
        if candidate.protectedSpell then PublishProtectedCastDiagnostics(candidate, event) end
        Fuyutsui:PublishAOEDiagnostic("castRejected", event.spellID)
        return false
    end
    if not candidate.timing and not candidate.duration then
        if candidate.protectedSpell then PublishProtectedCastDiagnostics(candidate, event) end
        Fuyutsui:PublishAOEDiagnostic("castRejected", event.spellID)
        return false
    end
    if candidate.protectedSpell then
        PublishProtectedCastDiagnostics(candidate, event)
    elseif not candidate.diagnosticsPublished then
        candidate.diagnosticsPublished = true
        Fuyutsui:PublishAOEDiagnostic("rawCast", event.spellID, candidate.spellID)
        Fuyutsui:PublishAOEDiagnostic("enemyCast", event.spellID, candidate.spellID)
    end
    return BindEventCast(
        event,
        candidate.unit,
        candidate.castGUID,
        candidate.spellID or event.spellID,
        candidate.isChannel,
        candidate.timing,
        candidate.protectedSpell)
end

function Fuyutsui:TryBindPendingAOECast(event)
    if not event or event.reservationOnly or event.cast or event.completed then return false end
    local now = GetTime()
    PrunePendingCasts(now)
    local threshold = self.AOEWarningConfig.protectedCorrelationSeconds
    local selectedIndex, selectedDistance
    for index, candidate in ipairs(warning.pendingCasts) do
        local distance = math.abs(event.createdAt - candidate.observedAt)
        local spellMatches = not candidate.spellID or candidate.spellID == event.spellID
        if spellMatches and distance <= threshold and (not selectedIndex or distance < selectedDistance) then
            selectedIndex, selectedDistance = index, distance
        end
    end
    if not selectedIndex then return false end
    local candidate = table.remove(warning.pendingCasts, selectedIndex)
    return BindPendingCandidate(event, candidate)
end

local function QueuePendingCast(unit, castGUID, spellID, isChannel, timing, duration, now, diagnosticsPublished)
    if not IsNameplateUnit(unit) or not timing and not duration then return end
    PrunePendingCasts(now)
    table.insert(warning.pendingCasts, {
        unit = unit,
        castGUID = SafeString(castGUID),
        spellID = spellID,
        isChannel = isChannel == true,
        timing = timing,
        duration = duration,
        observedAt = now,
        protectedSpell = false,
        diagnosticsPublished = diagnosticsPublished == true,
    })
end

local function FindDiagnosticEvent(now, endsAt)
    local selected, selectedDistance
    for _, event in pairs(warning.events) do
        if now <= event.expiresAt and not event.completed then
            local distance = endsAt and math.abs(event.impactAt - endsAt) or 0
            if not selected
                or distance < selectedDistance
                or (distance == selectedDistance and event.spellID and not selected.spellID)
                or (distance == selectedDistance and event.spellID == selected.spellID
                    and event.sequence < selected.sequence) then
                selected, selectedDistance = event, distance
            end
        end
    end
    return selected
end

-- DiGua's absorb warning is driven by its own UNIT_SPELLCAST_START frame. Keep
-- that path independent from the encounter timeline and from a readable
-- Spell ID; the timeline is only used later to merge the display row.
local function ObserveDiGuaAbsorbCast(unit, castGUID, spellID, isChannel)
    if isChannel or not IsLikelyAbsorbCastUnit(unit) then return false end
    local readableSpellID = SafeNumber(spellID)
    -- A readable non-1306517 cast is handled by the normal timeline/Spell ID
    -- matcher. The DiGua contextual fallback is reserved for protected values
    -- where the unit filters are the only reliable identity.
    if readableSpellID and readableSpellID ~= 1306517 then return false end

    local now = GetTime()
    local startedAt, endsAt = ReadCastTiming(unit, false)
    local timing = startedAt and endsAt and { startedAt = startedAt, endsAt = endsAt } or nil
    local duration = timing and nil or ReadProtectedCastDuration(unit, false)
    local impactAt = endsAt or (type(duration) == "number" and now + duration or nil)
    if not impactAt then
        TraceLog(
            "DiGua式读条收到但无法读取结束时间 unit=%s cast=%s spell=%s",
            tostring(unit), tostring(castGUID or "<protected>"), tostring(spellID or "<protected>"))
        return true
    end

    local event = FindRecentSemanticEvent(now)
    if not event or event.eventType ~= 2 then event = nil end
    if not event and readableSpellID then
        event = FindMatchingEvent(now, readableSpellID, impactAt)
    end
    if not event then
        for _, candidate in pairs(warning.events) do
            if candidate.eventType == 2
                and not candidate.cast
                and not candidate.completed
                and math.abs(candidate.impactAt - impactAt)
                    <= Fuyutsui.AOEWarningConfig.protectedCorrelationSeconds then
                event = candidate
                break
            end
        end
    end
    if not event then event = FindDirectCastEvent(2, 1306517, impactAt) end
    if not event then
        event = NewEvent(
            "cast:diguastart:" .. tostring(castGUID or unit .. ":" .. tostring(now)),
            2,
            impactAt,
            "cast",
            { spellID = 1306517, impactAnchor = "diguabar" })
    end
    if not event then return true end

    local protectedSpell = IsSecret(spellID) or readableSpellID ~= 1306517
    BindEventCast(
        event,
        unit,
        castGUID,
        readableSpellID or 1306517,
        false,
        timing,
        protectedSpell)
    if not timing then
        event.impactAnchor = "diguabar"
    end
    if protectedSpell then
        Fuyutsui:PublishAOEDiagnostic("rawCast", event.spellID)
        Fuyutsui:PublishAOEDiagnostic("protectedSpell", event.spellID)
    end
    Fuyutsui:PublishAOEDiagnostic("enemyCast", event.spellID, readableSpellID)
    TraceLog(
        "DiGua式读条绑定 event=%s unit=%s cast=%s spell=%s end=%.3f timeline=%s",
        tostring(event.runtimeID or event.id),
        tostring(unit),
        tostring(castGUID or "<protected>"),
        tostring(readableSpellID or "<protected>"),
        impactAt,
        event.source == "timeline" and "yes" or "no")
    return true
end

function Fuyutsui:ObserveAOEEnemyCast(unit, castGUID, spellID, isChannel)
    if type(unit) ~= "string" or unit == "player" then return end
    if ObserveDiGuaAbsorbCast(unit, castGUID, spellID, isChannel) then return end
    local now = GetTime()
    local diagnosticEvent = FindDiagnosticEvent(now)
    if IsSecret(spellID) then
        if not IsNameplateUnit(unit) then return end
        local startedAt, endsAt = ReadCastTiming(unit, isChannel)
        local timing = startedAt and endsAt and { startedAt = startedAt, endsAt = endsAt } or nil
        local duration = timing and nil or ReadProtectedCastDuration(unit, isChannel)
        local candidate = {
            unit = unit,
            castGUID = SafeString(castGUID),
            isChannel = isChannel == true,
            timing = timing,
            duration = duration,
            observedAt = now,
            protectedSpell = true,
        }
        if diagnosticEvent then
            PublishProtectedCastDiagnostics(candidate, diagnosticEvent)
        end
        local event = FindRecentSemanticEvent(now)
        if not event and self.state.diGuaBridgeReady == true
            and not isChannel
            and IsLikelyAbsorbCastUnit(unit) then
            local impactAt = endsAt or (type(duration) == "number" and now + duration or nil)
                or now + self.AOEWarningConfig.absorbDiGuaCastSeconds
                event = NewEvent(
                "cast:protected:" .. tostring(castGUID or unit .. ":" .. tostring(now)),
                2,
                impactAt,
                "cast",
                { spellID = 1306517, impactAnchor = "diguabar" })
            if event then
                Fuyutsui:PublishAOEDiagnostic("enemyCast", event.spellID)
                TraceLog(
                    "受保护读条直连 event=%s type=2 expectedSpell=1306517 unit=%s end=%s timeline=%s",
                    tostring(event.runtimeID or event.id),
                    tostring(unit),
                    tostring(endsAt or impactAt),
                    diagnosticEvent and "matched" or "missing")
            end
        end
        if event then
            if not event.cast then
                BindEventCast(
                    event,
                    candidate.unit,
                    candidate.castGUID,
                    event.spellID,
                    candidate.isChannel,
                    candidate.timing,
                    true)
            else
                BindPendingCandidate(event, candidate)
            end
        else
            PrunePendingCasts(now)
            table.insert(warning.pendingCasts, candidate)
        end
        return
    end

    spellID = SafeNumber(spellID)
    if not spellID then return end
    if diagnosticEvent then Fuyutsui:PublishAOEDiagnostic("rawCast", diagnosticEvent.spellID, spellID) end
    local canAttack = UnitCanAttack("player", unit)
    if IsSecret(canAttack) then
        if diagnosticEvent then
            Fuyutsui:PublishAOEDiagnostic("protectedHostility", diagnosticEvent.spellID, spellID)
        end
        if not IsNameplateUnit(unit) then return end
    elseif not canAttack then
        return
    end
    local startedAt, endsAt = ReadCastTiming(unit, isChannel)
    local timing = startedAt and endsAt and { startedAt = startedAt, endsAt = endsAt } or nil
    local duration = timing and nil or ReadProtectedCastDuration(unit, isChannel)
    diagnosticEvent = FindDiagnosticEvent(now, endsAt)
    if isChannel and not Fuyutsui.AOEWarningConfig.allowUnverifiedChannels then
        if diagnosticEvent then
            Fuyutsui:PublishAOEDiagnostic("enemyCast", diagnosticEvent.spellID, spellID)
            Fuyutsui:PublishAOEDiagnostic("castRejected", diagnosticEvent.spellID, spellID)
        end
        DebugLog("忽略未验证引导 spell=%d unit=%s", spellID, unit)
        return
    end

    local event = FindMatchingEvent(now, spellID, endsAt)
    local directEvent = false
    if not event and self.state.diGuaBridgeReady == true then
        local eventType = DirectCastEventType(spellID)
        local impactAt = endsAt or (type(duration) == "number" and now + duration or nil)
        -- Keep the direct path scoped to the heal-absorb mechanic. Ordinary
        -- AOE still requires its DiGua timeline semantic event so unrelated
        -- enemy casts cannot alter the existing priority chain.
        if eventType == 2 and impactAt then
            event = FindDirectCastEvent(eventType, spellID, impactAt)
            if not event then
                event = NewEvent(
                    "cast:" .. tostring(castGUID or unit .. ":" .. tostring(now)),
                    eventType,
                    impactAt,
                    "cast",
                    { spellID = spellID,
                        impactAnchor = eventType == 2 and "diguabar" or nil })
                directEvent = event ~= nil
            end
            if event then
                Fuyutsui:PublishAOEDiagnostic("enemyCast", event.spellID, spellID)
                TraceLog(
                    "真实读条直连 event=%s type=%d spell=%s unit=%s end=%s timeline=%s",
                    tostring(event.runtimeID or event.id),
                    event.eventType,
                    tostring(spellID),
                    tostring(unit),
                    tostring(endsAt or impactAt),
                    diagnosticEvent and "matched" or "missing")
            end
        end
    end
    if not event and diagnosticEvent then
        Fuyutsui:PublishAOEDiagnostic("enemyCast", diagnosticEvent.spellID, spellID)
        Fuyutsui:PublishAOEDiagnostic("castUnmatched", diagnosticEvent.spellID, spellID)
    elseif not event then
        QueuePendingCast(unit, castGUID, spellID, isChannel, timing, duration, now, false)
        return
    end
    if not diagnosticEvent and not directEvent and event.source ~= "cast" then
        QueuePendingCast(unit, castGUID, spellID, isChannel, timing, duration, now, true)
        return
    end
    if not event then
        return
    end
    if not timing and not duration then
        Fuyutsui:PublishAOEDiagnostic("castRejected", diagnosticEvent.spellID, spellID)
        return
    end
    BindEventCast(event, unit, castGUID, spellID, isChannel, timing, false)
end

local function CastMatches(cast, unit, castGUID, spellID)
    if not cast then return false end
    local safeGUID = SafeString(castGUID)
    if safeGUID and cast.castGUID then return safeGUID == cast.castGUID end
    -- Protected Spell IDs/GUIDs are not comparable in instanced combat. The
    -- unit is the stable identity for that cast until its terminal event.
    if cast.protectedTiming or cast.protectedSpellID then return cast.unit == unit end
    return cast.unit == unit and cast.spellID == SafeNumber(spellID)
end

local terminalPriority = {
    stopped = 1,
    succeeded = 2,
    interrupted = 3,
    failed = 3,
    failed_quiet = 3,
    died = 4,
}

local function CommitTerminal(id, cast, reason)
    local event = warning.events[id]
    if not event or event.cast ~= cast then return end
    warning.pendingTerminals[cast.key] = nil
    local now = GetTime()
    TraceLog(
        "读条终态 event=%s spell=%s cast=%s reason=%s accepted=true",
        tostring(event.runtimeID or event.id),
        tostring(cast.spellID or event.spellID or 0),
        tostring(cast.castGUID or "<protected>"),
        reason)
    if reason == "succeeded" then
        Fuyutsui:PublishAOEDiagnostic("castSucceeded", event.spellID, cast.spellID)
        ReleaseCast(event, true)
        event.status = "succeeded"
        event.castOutcome = "succeeded"
        local impactAt = cast.endsAt
        if event.eventType == 2 and not impactAt and event.impactAnchor ~= "diguabar" then
            event.status = "unknown"
            event.castOutcome = "missing_end_anchor"
            event.completed = false
            event.timelineFallbackBlocked = true
            event.expiresAt = math.max(
                event.expiresAt,
                now + Fuyutsui.AOEWarningConfig.absorbTimelineFallbackGraceSeconds
                    + Fuyutsui.AOEWarningConfig.impactActiveSeconds)
            TraceLog(
                "吸奶盾读条成功但缺少真实结束锚点 event=%s cast=%s outcome=unknown，禁止进入美德窗口",
                tostring(event.runtimeID or event.id),
                tostring(cast.castGUID or "<protected>"))
            return
        end
        event.completed = true
        TraceLog(
            "读条终态 event=%s spell=%s cast=%s reason=succeeded accepted=true status=succeeded",
            tostring(event.runtimeID or event.id),
            tostring(cast.spellID or event.spellID or 0),
            tostring(cast.castGUID or "<protected>"))
        -- The terminal callback can be processed after the cast actually ended.
        -- Anchor the post-cast delay to the observed cast end whenever it is
        -- available; protected casts use the explicit DiGua fallback anchor.
        impactAt = impactAt or event.impactAt or now
        event.impactAt = impactAt
        if cast.endsAt then event.impactAnchor = "actual" end
        event.virtueReadyAt = event.eventType == 2
            and impactAt + Fuyutsui.AOEWarningConfig.absorbVirtueDelaySeconds
            or impactAt
        event.expiresAt = impactAt
            + (event.eventType == 2 and Fuyutsui.AOEWarningConfig.absorbVirtueDelaySeconds or 0)
            + Fuyutsui.AOEWarningConfig.impactActiveSeconds
        if event.eventType == 2 then
            event.absorbObserved = false
            event.zeroAbsorbCycles = 0
            if Fuyutsui.ScheduleAOEHealAbsorbUpdate then
                Fuyutsui:ScheduleAOEHealAbsorbUpdate()
            end
            DebugLog(
                "吸奶盾读条成功，锚点=%s，结束后 %.2f 秒进入阶段3 event=%s spell=%d unitGUID=%s castGUID=%s",
                tostring(event.impactAnchor or "unknown"),
                Fuyutsui.AOEWarningConfig.absorbVirtueDelaySeconds,
                tostring(event.runtimeID or event.id),
                cast.spellID,
                cast.unitGUID,
                tostring(cast.castGUID))
        else
            DebugLog(
                "普通AOE读条成功 event=%s spell=%d unitGUID=%s castGUID=%s",
                tostring(event.runtimeID or event.id),
                cast.spellID,
                cast.unitGUID,
                tostring(cast.castGUID))
        end
        return
    end
    if reason == "died" then
        Fuyutsui:PublishAOEDiagnostic("castFailed", event.spellID, cast.spellID)
        event.status = "failed"
        event.castOutcome = "died"
        ReleaseCast(event, false)
        TraceLog(
            "读条终态 event=%s spell=%s cast=%s reason=died accepted=true status=failed",
            tostring(event.runtimeID or event.id),
            tostring(cast.spellID or event.spellID or 0),
            tostring(cast.castGUID or "<protected>"))
        RemoveEvent(id, "施法者死亡")
        return
    end

    if reason ~= "stopped" then
        Fuyutsui:PublishAOEDiagnostic("castFailed", event.spellID, cast.spellID)
        event.timelineFallbackBlocked = true
    end
    event.status = reason == "stopped" and "unknown" or "failed"
    event.castOutcome = reason
    ReleaseCast(event, false)
    TraceLog(
        "读条终态 event=%s spell=%s cast=%s reason=%s accepted=true status=%s",
        tostring(event.runtimeID or event.id),
        tostring(cast.spellID or event.spellID or 0),
        tostring(cast.castGUID or "<protected>"),
        reason,
        event.status)
    event.expiresAt = math.max(event.expiresAt, now + Fuyutsui.AOEWarningConfig.impactActiveSeconds)
    DebugLog(
        "%s，返回资源预留，等待重新读条 event=%s spell=%d unitGUID=%s castGUID=%s",
        reason == "stopped" and "读条结果未知" or "读条中断/失败",
        tostring(event.runtimeID or event.id),
        cast.spellID,
        cast.unitGUID,
        tostring(cast.castGUID))
end

local function QueueTerminal(event, reason)
    local cast = event and event.cast
    local priority = terminalPriority[reason]
    if not cast or not cast.key or not priority or warning.completedCasts[cast.key] then return end
    local pending = warning.pendingTerminals[cast.key]
    if pending then
        if priority > pending.priority then pending.reason, pending.priority = reason, priority end
        return
    end

    pending = { id = event.id, cast = cast, reason = reason, priority = priority }
    warning.pendingTerminals[cast.key] = pending
    -- STOP is emitted for both a completed cast and a canceled cast on some
    -- clients. Keep it pending for a short grace period so a late SUCCEEDED
    -- or INTERRUPTED event can replace the ambiguous signal.
    local delay = reason == "stopped"
        and Fuyutsui.AOEWarningConfig.castTerminalGraceSeconds
        or 0
    C_Timer.After(delay, function()
        local current = warning.pendingTerminals[cast.key]
        if current ~= pending then return end
        CommitTerminal(current.id, current.cast, current.reason)
    end)
end

function Fuyutsui:FinishAOEEnemyCast(unit, castGUID, spellID, reason)
    reason = reason or "stopped"
    for _, event in pairs(warning.events) do
        if CastMatches(event.cast, unit, castGUID, spellID) then
            QueueTerminal(event, reason)
            return
        end
    end
    TraceLog(
        "读条终态未匹配 unit=%s cast=%s spell=%s reason=%s",
        tostring(unit),
        tostring(castGUID or "<protected>"),
        tostring(spellID or 0),
        reason)
end

function Fuyutsui:CancelAOEEventsForUnitGUID(unitGUID)
    unitGUID = SafeString(unitGUID)
    if not unitGUID then return end
    for _, event in pairs(warning.events) do
        if event.cast and event.cast.unitGUID == unitGUID and not event.completed then
            QueueTerminal(event, "died")
        end
    end
end

function Fuyutsui:ConfirmAOEVirtue(spellID)
    if SafeNumber(spellID) ~= 200025 then return end
    for _, event in pairs(warning.events) do
        if event.eventType == 2
            and ((event.cast and event.cast.executionOpened) or event.completed) then
            event.virtueConfirmed = true
            TraceLog(
                "美德确认 event=%s status=%s",
                tostring(event.runtimeID or event.id),
                tostring(event.status or "unknown"))
        end
    end
end

function Fuyutsui:ObserveAOEHealAbsorbs()
    if not warning.absorbCalculator or not UnitGetDetailedHealPrediction then return end
    local anyPositive, readable = false, false
    for _, unit in ipairs(self.groupList or {}) do
        local member = self.group and self.group[unit]
        if member and member.valid then
            UnitGetDetailedHealPrediction(unit, nil, warning.absorbCalculator)
            local amount = SafeNumber(warning.absorbCalculator:GetHealAbsorbs())
            if amount then
                readable = true
                if amount > 0 then anyPositive = true end
            end
        end
    end
    if not readable then return end

    for id, event in pairs(warning.events) do
        if event.eventType == 2 then
            if anyPositive then
                if event.completed then
                    if not event.absorbObserved then
                        TraceLog(
                            "吸奶盾实际吸收首次出现 event=%s spell=%s",
                            tostring(event.runtimeID or id),
                            tostring(event.spellID or 0))
                    end
                    event.absorbObserved = true
                    event.zeroAbsorbCycles = 0
                else
                    TraceLog(
                        "吸收值变化但未收到读条成功 event=%s spell=%s，忽略数值兜底",
                        tostring(event.runtimeID or id),
                        tostring(event.spellID or 0))
                end
            elseif event.completed and event.absorbObserved then
                event.zeroAbsorbCycles = (event.zeroAbsorbCycles or 0) + 1
                -- Keep the execution window alive until Virtue is actually
                -- observed. Absorb pixels can clear before the queued key is
                -- accepted; removing the event here drops stage 3 and makes
                -- the runtime miss the only useful cast window.
                if event.zeroAbsorbCycles >= 2 and event.virtueConfirmed then
                    TraceLog(
                        "吸奶盾实际吸收结束 event=%s zeroCycles=%d",
                        tostring(event.runtimeID or id),
                        event.zeroAbsorbCycles)
                    RemoveEvent(id, "治疗吸收连续两次归零")
                end
            end
        end
    end
end

function Fuyutsui:ScheduleAOEHealAbsorbUpdate()
    if warning.absorbUpdatePending then return end
    warning.absorbUpdatePending = true
    C_Timer.After(0, function()
        warning.absorbUpdatePending = false
        Fuyutsui:ObserveAOEHealAbsorbs()
    end)
end

function Fuyutsui:ObserveAOEAudio(path)
    local eventType = SoundEventType(path)
    if not eventType then return end
    local now = GetTime()
    for _, event in pairs(warning.events) do
        if event.source == "timeline" and event.eventType == eventType and event.impactAt >= now then return end
    end
    local lead = eventType == 2 and Fuyutsui.AOEWarningConfig.absorbPrepareSeconds or 5
    NewEvent("audio:" .. tostring(now), eventType, now + lead, "audio")
end

function Fuyutsui:ObserveAOEDiGuaBar(iconID, duration, name, unitKey)
    iconID = SafeNumber(iconID)
    duration = SafeNumber(duration)
    name = SafeString(name)
    unitKey = SafeString(unitKey)
    if iconID ~= 132334 and name ~= "准备吸奶盾" then return end
    if not duration or duration <= 0 then return end

    local now = GetTime()
    local impactAt = now + duration
    for _, event in pairs(warning.events) do
        if event.eventType == 2
            and event.source == "diguabar"
            and not event.completed
            and math.abs(event.impactAt - impactAt) <= 0.5 then
            return
        end
    end
    local event = NewEvent(
        "diguabar:" .. tostring(unitKey or now),
        2,
        impactAt,
        "diguabar",
        { spellID = 1306517 })
    if event then
        event.diguaUnit = unitKey
        TraceLog(
            "DiGua倒计时已接管 event=%s unit=%s duration=%.2f",
            tostring(event.runtimeID or event.id),
            tostring(unitKey or "-"),
            duration)
    end
end

function Fuyutsui:CancelAOEDiGuaBar(unitKey)
    unitKey = SafeString(unitKey)
    for id, event in pairs(warning.events) do
        if event.source == "diguabar" and not event.completed
            and (not unitKey or event.diguaUnit == unitKey) then
            event.status = "failed"
            event.castOutcome = "diguabar_canceled"
            TraceLog(
                "DiGua倒计时取消 event=%s unit=%s",
                tostring(event.runtimeID or id), tostring(unitKey or "-"))
            RemoveEvent(id, "DiGua倒计时取消")
        end
    end
end

function Fuyutsui:ObserveAOETimelineEvent(eventInfo)
    if type(eventInfo) ~= "table" then return end
    local runtimeID = SafeNumber(eventInfo.id)
    if not runtimeID then return end
    local key = EventKey(runtimeID)
    if warning.events[key] then RemoveEvent(key, "event-id-reused") end
    warning.eventStates[runtimeID] = nil

    local remaining = GetEventRemaining(runtimeID) or SafeNumber(eventInfo.duration)
    if not remaining or remaining <= 0 then return end
    local eventType = EventNameType(SafeString(eventInfo.overrideName))
    if not eventType then
        eventType = EventNameType(SafeString(eventInfo.name))
            or EventNameType(SafeString(eventInfo.spellName))
    end
    if not eventType then return end

    local predictedImpact = GetTime() + remaining
    local timelineSpellID = SafeNumber(eventInfo.spellID)
    for _, existing in pairs(warning.events) do
        if existing.source == "cast"
            and existing.eventType == eventType
            and not existing.completed
            and existing.cast
            and existing.cast.endsAt
            and math.abs(existing.cast.endsAt - predictedImpact)
                <= Fuyutsui.AOEWarningConfig.protectedCorrelationSeconds
            and (not timelineSpellID or timelineSpellID <= 0 or existing.spellID == timelineSpellID) then
            existing.runtimeID = runtimeID
            existing.timelineRuntimeID = runtimeID
            TraceLog(
                "时间轴合并已有真实读条 event=%s runtime=%s type=%d spell=%s",
                tostring(existing.id),
                tostring(runtimeID),
                eventType,
                tostring(existing.spellID or timelineSpellID or 0))
            return
        end
    end

    NewEvent(key, eventType, predictedImpact, "timeline", {
        runtimeID = runtimeID,
        spellID = SafeNumber(eventInfo.spellID),
    })
end

local function ReadTimelineState(runtimeID)
    if not C_EncounterTimeline or not C_EncounterTimeline.GetEventState then return nil end
    local ok, state = pcall(C_EncounterTimeline.GetEventState, runtimeID)
    if not ok or IsSecret(state) then return nil end
    local states = Enum and Enum.EncounterTimelineEventState
    if states and state == states.Canceled then return "canceled" end
    if states and state == states.Finished then return "finished" end
    return "other"
end

function Fuyutsui:ObserveAOETimelineState(eventID)
    local runtimeID = SafeNumber(eventID)
    if not runtimeID then return end
    local state = ReadTimelineState(runtimeID)
    warning.eventStates[runtimeID] = state or "unknown"
    if state == "canceled" then
        local key = EventKey(runtimeID)
        local event = warning.events[key]
        if event and not event.completed then
            -- DiGua cancels both sides of a replacement pair while rebuilding
            -- the row. For heal-absorb warnings this is not evidence that the
            -- encounter mechanic was canceled; keep the verified impact time.
            if event.eventType == 2 then
                event.timelineCanceled = true
                event.expiresAt = math.max(
                    event.expiresAt,
                    GetTime() + Fuyutsui.AOEWarningConfig.impactActiveSeconds)
                DebugLog(
                    "吸奶盾时间轴收到取消，保留到点 event=%s remaining=%.2f",
                    tostring(event.runtimeID or event.id),
                    math.max(0, event.impactAt - GetTime()))
            else
                RemoveEvent(key, "DiGua主动取消")
            end
        end
    end
end

function Fuyutsui:RemoveAOETimelineEvent(eventID)
    local runtimeID = SafeNumber(eventID)
    if not runtimeID then return end
    local key = EventKey(runtimeID)
    local event = warning.events[key]
    local state = warning.eventStates[runtimeID]
    warning.eventStates[runtimeID] = nil
    if not event or event.completed then return end
    if state == "finished" then
        event.timelineFinished = true
        return
    end
    -- DiGua may remove a replacement timeline row while the correlated enemy
    -- cast is still active. Preserve that cast unless cancellation is explicit.
    if event.cast and state ~= "canceled" then
        event.timelineFinished = true
        return
    end
    -- Absorb timeline rows are often replaced by DiGua before the protected
    -- cast/impact signal arrives. Keep the reservation until its verified time.
    if event.eventType == 2 then
        event.timelineFinished = true
        if state == "canceled" then event.timelineCanceled = true end
        event.expiresAt = math.max(
            event.expiresAt,
            event.impactAt + Fuyutsui.AOEWarningConfig.absorbTimelineFallbackGraceSeconds
                + Fuyutsui.AOEWarningConfig.impactActiveSeconds,
            GetTime() + Fuyutsui.AOEWarningConfig.impactActiveSeconds)
        DebugLog(
            "吸奶盾时间轴移除，保留到点 event=%s state=%s remaining=%.2f",
            tostring(event.runtimeID or event.id),
            tostring(state or "unknown"),
            math.max(0, event.impactAt - GetTime()))
        return
    end
    RemoveEvent(key, state == "canceled" and "DiGua主动取消" or "时间轴移除状态不可判定")
end

function Fuyutsui:ClearAOEWarningEvents(reason)
    local ids = {}
    for id in pairs(warning.events) do table.insert(ids, id) end
    for _, id in ipairs(ids) do RemoveEvent(id, reason or "兜底清理") end
    warning.eventStates = {}
    warning.pendingTerminals = {}
    warning.pendingCasts = {}
    warning.polledCasts = {}
    warning.castPollElapsed = 0
    warning.castOwners = {}
    warning.completedCasts = {}
    SetOutput(0, 0)
end

function Fuyutsui:InitializeAOEWarning()
    if warning.initialized then return end
    warning.initialized = true
    self.state.aoeEventType = 0
    self.state.aoeEventStage = 0
    self.state.aoeProtectedCastActive = false
    self.state.divineTollExpectedReady = false
    if CreateUnitHealPredictionCalculator then
        warning.absorbCalculator = CreateUnitHealPredictionCalculator()
    end

    -- Keep the DiGua-style cast source on a dedicated frame. The central
    -- Shigure event frame also receives these events, but a separate listener
    -- makes the absorb monitor independent of unrelated event dispatch work.
    if CreateFrame then
        warning.castEventFrame = CreateFrame("Frame", "FuyutsuiAOECastEventFrame")
        warning.castEventFrame:RegisterEvent("UNIT_SPELLCAST_START")
        warning.castEventFrame:RegisterEvent("UNIT_SPELLCAST_STOP")
        warning.castEventFrame:RegisterEvent("UNIT_SPELLCAST_INTERRUPTED")
        warning.castEventFrame:RegisterEvent("UNIT_SPELLCAST_FAILED")
        warning.castEventFrame:RegisterEvent("UNIT_SPELLCAST_FAILED_QUIET")
        warning.castEventFrame:RegisterEvent("UNIT_SPELLCAST_SUCCEEDED")
        warning.castEventFrame:RegisterEvent("UNIT_SPELLCAST_CHANNEL_START")
        warning.castEventFrame:RegisterEvent("UNIT_SPELLCAST_CHANNEL_STOP")
        warning.castEventFrame:SetScript("OnEvent", function(_, event, unit, castGUID, spellID)
            if type(unit) ~= "string" or unit == "player" then return end
            if not IsNameplateUnit(unit) then return end
            TraceLog(
                "DiGua式事件帧收到 event=%s unit=%s cast=%s spell=%s",
                tostring(event), tostring(unit), tostring(castGUID or "<protected>"),
                tostring(spellID or "<protected>"))
            if event == "UNIT_SPELLCAST_START" then
                Fuyutsui:ObserveAOEEnemyCast(unit, castGUID, spellID, false)
            elseif event == "UNIT_SPELLCAST_CHANNEL_START" then
                Fuyutsui:ObserveAOEEnemyCast(unit, castGUID, spellID, true)
            elseif event == "UNIT_SPELLCAST_STOP" then
                Fuyutsui:FinishAOEEnemyCast(unit, castGUID, spellID, "stopped")
            elseif event == "UNIT_SPELLCAST_INTERRUPTED" then
                Fuyutsui:FinishAOEEnemyCast(unit, castGUID, spellID, "interrupted")
            elseif event == "UNIT_SPELLCAST_FAILED" then
                Fuyutsui:FinishAOEEnemyCast(unit, castGUID, spellID, "failed")
            elseif event == "UNIT_SPELLCAST_FAILED_QUIET" then
                Fuyutsui:FinishAOEEnemyCast(unit, castGUID, spellID, "failed_quiet")
            elseif event == "UNIT_SPELLCAST_SUCCEEDED" then
                Fuyutsui:FinishAOEEnemyCast(unit, castGUID, spellID, "succeeded")
            elseif event == "UNIT_SPELLCAST_CHANNEL_STOP" then
                Fuyutsui:FinishAOEEnemyCast(unit, castGUID, spellID, "stopped")
            end
        end)
        warning.castEventFrame:SetScript("OnUpdate", function(_, elapsed)
            warning.castPollElapsed = warning.castPollElapsed + (elapsed or 0)
            if warning.castPollElapsed < 0.05 then return end
            warning.castPollElapsed = 0
            if type(UnitCastingInfo) ~= "function" then return end

            local seen = {}
            for index = 1, 40 do
                local unit = "nameplate" .. index
                local name, _, _, rawStart, rawEnd, _, castID, _, rawSpellID = UnitCastingInfo(unit)
                if name then
                    local startedAt, endsAt = SafeNumber(rawStart), SafeNumber(rawEnd)
                    local castGUID = SafeString(castID)
                    local spellID = rawSpellID
                    seen[unit] = true
                    if not warning.polledCasts[unit] then
                        warning.polledCasts[unit] = true
                        TraceLog(
                            "DiGua式轮询发现读条 unit=%s cast=%s spell=%s start=%s end=%s",
                            unit,
                            tostring(castGUID or "<protected>"),
                            tostring(spellID or "<protected>"),
                            tostring(startedAt or "<protected>"),
                            tostring(endsAt or "<protected>"))
                        Fuyutsui:ObserveAOEEnemyCast(unit, castGUID, spellID, false)
                    end
                end
            end
            for unit in pairs(warning.polledCasts) do
                if not seen[unit] then warning.polledCasts[unit] = nil end
            end
        end)
    end

    if hooksecurefunc then
        hooksecurefunc("PlaySoundFile", function(path) Fuyutsui:ObserveAOEAudio(path) end)
    end
end
