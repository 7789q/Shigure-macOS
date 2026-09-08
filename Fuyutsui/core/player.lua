local addon, ns = ...

local IsSpellKnown = C_SpellBook.IsSpellKnown
local IsSpellInSpellBook = C_SpellBook.IsSpellInSpellBook

local state = Fuyutsui.state
local EnumPowerType = Fuyutsui.EnumPowerType
local spellsList = Fuyutsui.spellsList

local drinkStatusTimer = nil

local BLOOD_BOIL_SPELL_ID = 50842
local BLOOD_BOIL_HIGHLIGHT_AURA_IDS = { 1265968, 1265982 }
local BLOOD_BOIL_PROC_WINDOW_SECONDS = 15
local BLOOD_BOIL_ECHO_WINDOW_SECONDS = 3
local BLOOD_BOIL_GLOW_GRACE_SECONDS = 1
local BLOOD_BOIL_DUPLICATE_CAST_SECONDS = 0.25
local DEATHBRINGER_BURST_WINDOW_SECONDS = 3
local deathbringerBurstWindowTimer = nil

local IsSpellOverlayed = C_SpellActivationOverlay and C_SpellActivationOverlay.IsSpellOverlayed
    or IsSpellOverlayed

local function IsSecret(value)
    return issecretvalue and issecretvalue(value)
end

local function BaseOf(spellID)
    if not spellID or IsSecret(spellID) then return nil end
    if C_Spell and C_Spell.GetBaseSpell then
        local ok, baseSpellID = pcall(C_Spell.GetBaseSpell, spellID)
        if ok and baseSpellID then return baseSpellID end
    end
    if C_SpellBook and C_SpellBook.FindBaseSpellByID then
        local ok, baseSpellID = pcall(C_SpellBook.FindBaseSpellByID, spellID)
        if ok and baseSpellID then return baseSpellID end
    end
end

local function LiveID(spellID)
    if not spellID or IsSecret(spellID) then return nil end
    if C_Spell and C_Spell.GetOverrideSpell then
        local ok, liveSpellID = pcall(C_Spell.GetOverrideSpell, spellID)
        if ok and liveSpellID then return liveSpellID end
    end
    if C_SpellBook and C_SpellBook.FindSpellOverrideByID then
        local ok, liveSpellID = pcall(C_SpellBook.FindSpellOverrideByID, spellID)
        if ok and liveSpellID then return liveSpellID end
    end
end

local function FindPlayerAuraBySpellID(spellID)
    if C_UnitAuras and C_UnitAuras.GetAuraDataByIndex then
        for index = 1, 40 do
            local ok, aura = pcall(
                C_UnitAuras.GetAuraDataByIndex,
                "player",
                index,
                "HELPFUL"
            )
            if not ok then
                break
            end
            if not aura then break end
            local auraSpellID = aura.spellId
            if not IsSecret(auraSpellID)
                and type(auraSpellID) == "number"
                and auraSpellID == spellID then
                return aura
            end
        end
    end
    if AuraUtil and AuraUtil.FindAuraBySpellID then
        local ok, aura = pcall(AuraUtil.FindAuraBySpellID, spellID, "player", "HELPFUL")
        if ok then return aura end
    end
end

function Fuyutsui:IsBloodBoilSpell(spellID)
    if not spellID or IsSecret(spellID) then return false end
    if spellID == BLOOD_BOIL_SPELL_ID then return true end
    for _, auraID in ipairs(BLOOD_BOIL_HIGHLIGHT_AURA_IDS) do
        if spellID == auraID then return true end
    end
    if BaseOf(spellID) == BLOOD_BOIL_SPELL_ID then return true end
    return LiveID(BLOOD_BOIL_SPELL_ID) == spellID
end

local function IsBloodBoilAuraActive()
    for _, spellID in ipairs(BLOOD_BOIL_HIGHLIGHT_AURA_IDS) do
        if FindPlayerAuraBySpellID(spellID) then
            return true
        end
    end
    return false
end

local function IsBloodBoilOverlayLive()
    if not IsSpellOverlayed then return nil end
    local liveSpellID = LiveID(BLOOD_BOIL_SPELL_ID)
    local spellIDs = { BLOOD_BOIL_SPELL_ID, liveSpellID }
    for _, spellID in ipairs(spellIDs) do
        if spellID and not IsSecret(spellID) then
            local ok, active = pcall(IsSpellOverlayed, spellID)
            if ok and active then return true end
        end
    end
    return false
end

function Fuyutsui:IsBloodBoilHighlightActive()
    local overlayLive = IsBloodBoilOverlayLive()
    if overlayLive == true then return true end
    return IsBloodBoilAuraActive()
end

function Fuyutsui:SetBloodBoilHighlightActive(active)
    local changed = state.bloodBoilHighlightActive ~= active
    state.bloodBoilHighlightActive = active
    if changed then
        self:UpdateStateBlock("状态", "血沸手动触发")
        self:UpdateStateBlock("状态", "血沸高亮")
        self:UpdateStateBlock("状态", "血沸自动链")
    end
end

function Fuyutsui:SetBloodBoilAutoChain(active)
    if state.bloodBoilAutoChain == active then return end
    state.bloodBoilAutoChain = active
    self:UpdateStateBlock("状态", "血沸自动链")
end

function Fuyutsui:StartBloodBoilProcWindow(now)
    state.bloodBoilManualTriggerPending = true
    state.bloodBoilPhase = "PROC"
    state.bloodBoilProcWindowUntil = now + BLOOD_BOIL_PROC_WINDOW_SECONDS
    state.bloodBoilEchoWindowUntil = nil
    state.bloodBoilPendingTriggers = 0
    state.bloodBoilRepeatHighlight = false
    self:UpdateStateBlock("状态", "血沸手动触发")
end

function Fuyutsui:StartBloodBoilEchoWindow(now)
    state.bloodBoilManualTriggerPending = false
    self:SetBloodBoilAutoChain(true)
    state.bloodBoilPhase = "ECHO"
    state.bloodBoilEchoWindowUntil = now + BLOOD_BOIL_ECHO_WINDOW_SECONDS
    state.bloodBoilRepeatHighlight = false
    self:UpdateStateBlock("状态", "血沸手动触发")
end

function Fuyutsui:HandleBloodBoilOverlay(active, spellID)
    if spellID and not IsSecret(spellID) and not self:IsBloodBoilSpell(spellID) then
        return
    end

    local now = GetTime()
    if not active then
        local stillActive = self:IsBloodBoilHighlightActive()
        state.bloodBoilOverlayOn = stillActive
        state.bloodBoilLastOverlayHideAt = now
        self:SetBloodBoilHighlightActive(stillActive)
        return
    end

    local risingEdge = not state.bloodBoilOverlayOn
    state.bloodBoilOverlayOn = true
    self:SetBloodBoilHighlightActive(true)
    if not risingEdge then return end
    if state.bloodBoilPhase then return end

    self:StartBloodBoilProcWindow(now)
end

function Fuyutsui:RefreshBloodBoilOverlayState()
    local active = self:IsBloodBoilHighlightActive()
    self:HandleBloodBoilOverlay(active)
end

function Fuyutsui:BloodBoilGlowGate()
    local now = GetTime()
    local overlayLive = IsBloodBoilOverlayLive()
    if overlayLive == true or state.bloodBoilOverlayOn then
        return true
    end
    if state.bloodBoilLastOverlayHideAt
        and now - state.bloodBoilLastOverlayHideAt <= BLOOD_BOIL_GLOW_GRACE_SECONDS then
        return true
    end
    return IsBloodBoilAuraActive()
end

function Fuyutsui:ConfirmBloodBoilSpellcast(spellID)
    if not self:IsBloodBoilSpell(spellID) then return end
    local now = GetTime()
    if state.bloodBoilLastCastAt
        and now - state.bloodBoilLastCastAt <= BLOOD_BOIL_DUPLICATE_CAST_SECONDS then
        return
    end
    state.bloodBoilLastCastAt = now

    if state.bloodBoilPhase == "PROC" then
        self:StartBloodBoilEchoWindow(now)
        self:UpdateStateBlock("状态", "血沸手动触发")
        return
    end
    if state.bloodBoilPhase == "ECHO" then
        state.bloodBoilEchoWindowUntil = now + BLOOD_BOIL_ECHO_WINDOW_SECONDS
        return
    end
end

function Fuyutsui:UpdateBloodBoilAutomationState()
    local now = GetTime()
    if state.bloodBoilPhase == "PROC"
        and state.bloodBoilProcWindowUntil
        and now > state.bloodBoilProcWindowUntil then
        state.bloodBoilPhase = nil
        state.bloodBoilProcWindowUntil = nil
        state.bloodBoilManualTriggerPending = false
        state.bloodBoilPendingTriggers = 0
        state.bloodBoilRepeatHighlight = false
        self:UpdateStateBlock("状态", "血沸手动触发")
    elseif state.bloodBoilPhase == "ECHO"
        and state.bloodBoilEchoWindowUntil
        and now > state.bloodBoilEchoWindowUntil then
        state.bloodBoilPhase = nil
        state.bloodBoilEchoWindowUntil = nil
        state.bloodBoilManualTriggerPending = false
        state.bloodBoilPendingTriggers = 0
        state.bloodBoilRepeatHighlight = false
        self:SetBloodBoilAutoChain(false)
        self:UpdateStateBlock("状态", "血沸手动触发")
    end
end

function Fuyutsui:ResetBloodBoilAutomation()
    state.bloodBoilManualTriggerPending = false
    state.bloodBoilAutoChain = false
    state.bloodBoilHighlightActive = false
    state.bloodBoilOverlayOn = false
    state.bloodBoilPhase = nil
    state.bloodBoilProcWindowUntil = nil
    state.bloodBoilEchoWindowUntil = nil
    state.bloodBoilPendingTriggers = 0
    state.bloodBoilRepeatHighlight = false
    state.bloodBoilLastOverlayHideAt = nil
    state.bloodBoilLastCastAt = nil
    self:UpdateStateBlock("状态", "血沸手动触发")
    self:UpdateStateBlock("状态", "血沸自动链")
end

function Fuyutsui:MarkDeathbringerBurstWindow()
    if deathbringerBurstWindowTimer then
        deathbringerBurstWindowTimer:Cancel()
    end
    state.deathbringerBurstWindow = true
    self:UpdateStateBlock("状态", "符文刃舞爆发窗口")
    deathbringerBurstWindowTimer = C_Timer.NewTimer(DEATHBRINGER_BURST_WINDOW_SECONDS, function()
        state.deathbringerBurstWindow = false
        deathbringerBurstWindowTimer = nil
        self:UpdateStateBlock("状态", "符文刃舞爆发窗口")
    end)
end

function Fuyutsui:IsDeathbringerBurstSpell(spellID)
    if not spellID or IsSecret(spellID) then return false end
    return spellID == 49028 or BaseOf(spellID) == 49028 or LiveID(49028) == spellID
end

function Fuyutsui:ResetDeathbringerBurstWindow()
    if deathbringerBurstWindowTimer then
        deathbringerBurstWindowTimer:Cancel()
        deathbringerBurstWindowTimer = nil
    end
    if not state.deathbringerBurstWindow then return end
    state.deathbringerBurstWindow = false
    self:UpdateStateBlock("状态", "符文刃舞爆发窗口")
end

function Fuyutsui:GetCharacterInfo()
    self.db.char.level = UnitLevel("player")
    self.state.name = UnitName("player")
    self.state.GUID = UnitGUID("player")
    self.state.classColor = RAID_CLASS_COLORS[self.state.classFilename].colorStr
end

function Fuyutsui:GetCharacterSpecInfo()
    self:MacroTrace("GetCharacterSpecInfo 进入")
    self.state.specIndex = C_SpecializationInfo.GetSpecialization()
    local specID, specName, _, _, role = C_SpecializationInfo.GetSpecializationInfo(self.state.specIndex)
    self.state.specID = specID
    self.state.specName = specName
    self.state.specRole = role
    self.state.specRange = self.rangeSpecID[specID]
    self.state.isDead = UnitIsDeadOrGhost("player")
    self.state.isChatOpen = false
    self.state.casting = false
    self.state.channeling = false
    self.state.mountCasting = false
    self:LoadPlayerBlocks(self.state.specIndex)
    self:MacroTrace("LoadPlayerBlocks 完成：specIndex=%s blocks=%s", tostring(self.state.specIndex), tostring(self.blocks ~= nil))
    self:UpdateSpellKnown()
    self:UpdatePlayerMounted()
    self:UpdateGroup()
    self:MacroTrace("UpdateGroup 完成：group=%s", tostring(self.group ~= nil))
    -- 登录阶段其他插件可能稍后写入覆盖绑定；首次加载延后 5 秒，确保本插件最后绑定。
    C_Timer.After(5, function()
        self:MacroTrace("登录延迟宏绑定回调触发")
        self:LoadPlayerMacros()
    end)
    self:MacroTrace("已安排登录延迟宏绑定：5 秒")
    self:GetItemCount()
    self:UpdateStateBlock("状态", "职业")
    self:UpdateStateBlock("状态", "专精")
end

function Fuyutsui:UpdatePlayerSpecInfo()
    self:MacroTrace("UpdatePlayerSpecInfo 进入")
    self:ResetBloodBoilAutomation()
    self:ClearAllTextures()
    self.state.specIndex = C_SpecializationInfo.GetSpecialization()
    local specID, specName, _, _, role = C_SpecializationInfo.GetSpecializationInfo(self.state.specIndex)
    self.state.specID = specID
    self.state.specName = specName
    self.state.specRole = role
    self.state.specRange = self.rangeSpecID[specID]
    self:LoadPlayerBlocks(self.state.specIndex)
    self:UpdateSpellKnown()
    self:UpdatePlayerBlocks()
    self:LoadPlayerMacros()
    self:MacroTrace("UpdatePlayerSpecInfo 宏绑定调用返回")
    self:UpdateStateBlock("状态", "职业")
    self:UpdateStateBlock("状态", "专精")
end

function Fuyutsui:UpdatePlayerValid()
    local reason = 1
    if state.isDead then
        reason = 2
    elseif state.mounted then
        reason = 3
    elseif state.isChatOpen then
        reason = 4
    elseif state.drinkStatus then
        reason = 5
    elseif state.mountCasting then
        reason = 6
    end
    state.valid = reason / 255
    self:UpdateStateBlock("状态", "有效性")
end

function Fuyutsui:UpdatePlayerCombat()
    local inCombat = UnitAffectingCombat("player")
    if inCombat and not state.combat then
        state.combatStartTime = GetTime()
    end
    state.combat = inCombat
end

function Fuyutsui:UpdatePlayerCombatTime()
    if state.combat then
        local combatTime = GetTime() - state.combatStartTime
        state.combatTime = math.max(1 / 255, math.min(1, combatTime / 255))
    else
        state.combatTime = 0
    end
    self:UpdateStateBlock("状态", "战斗时间")
end

function Fuyutsui:UpdatePlayerMoving(boolean)
    state.drinkStatus = false
    self:UpdatePlayerValid()
    if boolean then
        state.stationarySince = nil
    elseif not state.stationarySince then
        state.stationarySince = GetTime()
    end
    state.moving = boolean and 1 / 255 or 0
    self:UpdateStateBlock("状态", "移动")
    self:UpdatePlayerStationaryDuration()
end

function Fuyutsui:UpdatePlayerStationaryDuration()
    local duration = 0
    if state.moving == 0 and state.stationarySince then
        duration = math.max(0, math.min(255, GetTime() - state.stationarySince)) / 255
    end
    state.stationaryDuration = duration
    self:UpdateStateBlock("状态", "站定时长")
end

function Fuyutsui:UpdatePlayerCastBlocks()
    self:UpdateStateBlock("状态", "施法(正计时)")
    self:UpdateStateBlock("状态", "施法(倒计时)")
    self:UpdateStateBlock("状态", "引导")
    self:UpdateStateBlock("状态", "蓄力")
    self:UpdateStateBlock("状态", "蓄力层数")
end

function Fuyutsui:UpdatePlayerCastingInfo()
    self:UpdateStateBlock("状态", "施法(正计时)")
    self:UpdateStateBlock("状态", "施法(倒计时)")
end

function Fuyutsui:UpdatePlayerChannelingInfo()
    self:UpdateStateBlock("状态", "引导")
end

function Fuyutsui:UpdatePlayerEmpowerInfo()
    self:UpdateStateBlock("状态", "蓄力")
    self:UpdateStateBlock("状态", "蓄力层数")
end

function Fuyutsui:UpdatePlayerHealth()
    local healthPercent = UnitHealthPercent("player", false, self.curve100)
    ---@diagnostic disable-next-line: param-type-mismatch
    local _, _, b = healthPercent:GetRGB()
    state.healthPercent = b
    self:UpdateStateBlock("状态", "生命值")
end

function Fuyutsui:UpdatePlayerPower(powerType)
    local blocks = self.blocks
    if not blocks then return end
    local powerName = self.powerNameMap[powerType]
    local power = UnitPower("player", EnumPowerType[powerType])
    if not powerName then return end
    if issecretvalue(power) then
        if not self.powerCurves[powerType] then self:CreatePowerCurve(powerType) end
        local powerPercent = UnitPowerPercent("player", EnumPowerType[powerType], nil, self.powerCurves[powerType])
        ---@diagnostic disable-next-line: param-type-mismatch
        local _, _, b = powerPercent:GetRGB()
        state.power[powerType] = b
        self:UpdateBareStateBlock(powerName, { "能量", "状态" })
    else
        state.power[powerType] = power / 255
        self:UpdateBareStateBlock(powerName, { "能量", "状态" })
    end
end

function Fuyutsui:UpdateChargedComboPoints()
    local chargedPoints = GetUnitChargedPowerPoints("player")
    state.chargedComboPoints = (chargedPoints and #chargedPoints or 0) / 255
    self:UpdateStateBlock("能量", "增压层数")
end

function Fuyutsui:UpdatePlayerPowerType()
    state.power = {}
    for powerType in pairs(EnumPowerType) do
        self:CreatePowerCurve(powerType)
        self:UpdatePlayerPower(powerType)
    end
end

local empowerSpellId = {
    [355936] = true,  -- 梦境吐息
    [357208] = true,  -- 火焰吐息
    [382266] = true,  -- 火焰吐息
    [382411] = true,  -- 永恒之涌
    [396286] = true,  -- 地壳激变
    [1263824] = true, -- 吞噬
}
local assistantWasEmpower = false
local assistantSuppressUntil = 0

function Fuyutsui:UpdatePlayerAssistant()
    local spellId = C_AssistedCombat.GetNextCastSpell()
    local now = GetTime()

    -- 离开蓄力推荐后，强制显示 0 持续 0.5 秒
    if assistantSuppressUntil > 0 and now < assistantSuppressUntil then
        if empowerSpellId[spellId] then
            assistantSuppressUntil = 0
        else
            state.assistantSpell = 0
            self:UpdateStateBlock("状态", "一键辅助")
            return
        end
    else
        assistantSuppressUntil = 0
    end

    if empowerSpellId[spellId] then
        assistantWasEmpower = true
        local spellIndex = spellsList[spellId] and spellsList[spellId].index or 0
        state.assistantSpell = spellIndex / 255 or 0
        self:UpdateStateBlock("状态", "一键辅助")
        return
    end

    if assistantWasEmpower then
        assistantWasEmpower = false
        assistantSuppressUntil = now + 0.7
        state.assistantSpell = 0
        self:UpdateStateBlock("状态", "一键辅助")
        return
    end

    local spellIndex = spellsList[spellId] and spellsList[spellId].index or 0
    state.assistantSpell = spellIndex / 255 or 0
    self:UpdateStateBlock("状态", "一键辅助")
end

function Fuyutsui:UpdateGroupType()
    local index = 0
    if self:IsRaidGroup() then
        index = UnitInRaid("player") or 0
    elseif UnitInParty("player") then
        index = 46
    end
    state.groupType = index / 255 or 0
    self:UpdateStateBlock("状态", "队伍类型")
end

function Fuyutsui:IsRaidGroup()
    return UnitInRaid("player") ~= nil
end

function Fuyutsui:UpdateGroupCount()
    local count = GetNumGroupMembers()
    state.groupCount = count / 255 or 0
    self:UpdateStateBlock("状态", "队伍人数")
end

function Fuyutsui:UpdateInstanceState()
    local inInstance = false
    if type(IsInInstance) == "function" then
        inInstance = IsInInstance() == true
    end
    state.inInstance = inInstance
    self:UpdateStateBlock("状态", "副本内")
end

function Fuyutsui:UpdateEncounterID(encounterID, difficultyID)
    state.encounterID = encounterID
    local id = self.bossID and self.bossID[encounterID] or 0
    if id then
        state.bossID = id / 255 or 0
    else
        state.bossID = 0
    end
    self:UpdateStateBlock("状态", "首领战")
    state.difficultyID = difficultyID
    self:UpdateStateBlock("状态", "难度")
end

function Fuyutsui:UpdateHeroTalent()
    if self.heroTalents then
        C_Timer.After(1, function()
            self.state.heroTalent = 0
            for spellID, index in pairs(self.heroTalents) do
                if IsSpellKnown(spellID) or IsSpellInSpellBook(spellID) then
                    self.state.heroTalent = index
                    break
                end
            end
            self:UpdateStateBlock("状态", "英雄天赋")
        end)
    end
end

function Fuyutsui:UpdatePlayerBarInfo()
    local blocks = self.blocks
    if self.RefreshPlayerAuraContainers then
        self:RefreshPlayerAuraContainers()
    end
    if blocks and blocks.bars then
        for _, v in ipairs(blocks.bars) do
            self:CreateAutoLayoutBar(v.valueType, v.minValue, v.maxValue, v.spellId)
        end
    end
    if self.LayoutAuraApplicationBars then
        self:LayoutAuraApplicationBars()
    end
end

function Fuyutsui:UpdatePlayerMounted()
    state.mounted = IsMounted() or state.shapeshiftFormID == 27 or state.shapeshiftFormID == 3 or
        state.shapeshiftFormID == 29
    self:UpdatePlayerValid()
end

function Fuyutsui:UpdatePlayerCasting(spellId)
    local castingSpell = spellsList[spellId] and spellsList[spellId].index or 0
    state.castingSpell = castingSpell / 255 or 0
    self:UpdateStateBlock("状态", "施法目标")
    self:UpdateStateBlock("状态", "施法技能")
end

function Fuyutsui:PublishPlayerAction(spellId, status)
    local spellIndex = 0
    local spell
    if not issecretvalue(spellId) then
        if type(spellId) ~= "number" then return end
        spell = spellsList[spellId]
        if spell and type(spell.index) == "number" then
            spellIndex = spell.index
        end
    end

    -- The queue slot is committed by its serial block after its payload blocks.
    -- This prevents the screen reader from accepting a half-written tuple.
    local serial = ((state.playerActionSerial or 0) % 255) + 1
    local slot = ((serial - 1) % 4) + 1
    state.playerActionSerial = serial
    if spellIndex == 0 then
        state.playerActionSpell = 0
    else
        state.playerActionSpell = spellIndex
    end
    state.playerActionStatus = status
    state.playerActionEvents = state.playerActionEvents or {}
    state.playerActionEvents[slot] = {
        serial = serial,
        spell = spellIndex,
        status = status,
    }

    if spell then
        if state.specIndex == 1 and status == 2 then
            if spell.name == "心脏打击" then
                state.bloodBoilHeartStrikeCount = math.min(3, (state.bloodBoilHeartStrikeCount or 0) + 1)
                self:UpdateStateBlock("状态", "血沸循环心打次数")
            elseif spell.name == "血液沸腾" then
                state.bloodBoilHeartStrikeCount = 0
                self:UpdateStateBlock("状态", "血沸循环心打次数")
            end
        end
    end

    self:UpdateStateBlock("状态", "玩家动作技能")
    self:UpdateStateBlock("状态", "玩家动作状态")
    self:UpdateStateBlock("状态", "玩家动作序号")
    self:UpdateStateBlock("状态", "玩家动作事件" .. slot .. "技能")
    self:UpdateStateBlock("状态", "玩家动作事件" .. slot .. "状态")
    self:UpdateStateBlock("状态", "玩家动作事件" .. slot .. "序号")
end

function Fuyutsui:ResetBloodBoilCycleCount()
    if state.bloodBoilHeartStrikeCount == 0 then return end
    state.bloodBoilHeartStrikeCount = 0
    self:UpdateStateBlock("状态", "血沸循环心打次数")
end

function Fuyutsui:UpdatePlayerConfig()
    if not (self.db and self.db.char) then return end
    local names = { "爆发开关", "AOE开关", "输出模式", "爆发药水开关" }
    for i = 1, #names do
        self:UpdateBareStateBlock(names[i], { "配置开关", "状态" })
    end
end

function Fuyutsui:UpdatePlayerStagger()
    local unit = "player"
    local damage = UnitStagger(unit)
    local maxHealth = UnitHealthMax(unit)
    if issecretvalue(damage) or issecretvalue(maxHealth) then
        state.staggerPercent = 0
        self:UpdateStateBlock("状态", "酒池")
        return
    end
    local staggerPercent = damage / maxHealth * 100
    state.staggerPercent = staggerPercent / 255 or 0
    self:UpdateStateBlock("状态", "酒池")
end

local holyArmaments = {
    [432459] = 1, -- 神圣壁垒
    [432472] = 2, -- 圣洁武器
}

function Fuyutsui:UpdateHolyArmaments(spellID) -- 神圣军备
    if not spellID or spellID ~= 375576 then return end
    for spellId, index in pairs(holyArmaments) do
        local overrideSpellID = C_Spell.GetOverrideSpell(375576)
        if not overrideSpellID then return end
        if overrideSpellID == spellId then
            state.holyArmaments = index / 255 or 0
            self:UpdateStateBlock("状态", "神圣军备")
        end
    end
end

function Fuyutsui:UpdateReaverGlaive(spellID) -- 收割者战刃
    if not spellID or spellID ~= 204157 then return end
    local overrideSpellID = C_Spell.GetOverrideSpell(204157)

    if overrideSpellID == 1283344 then
        state.reaverGlaive = 1 / 255
        self:UpdateStateBlock("状态", "收割者战刃")
    else
        state.reaverGlaive = 0
        self:UpdateStateBlock("状态", "收割者战刃")
    end
end

local heroicStrikeTimer = nil

function Fuyutsui:UpdateHeroicStrike(spellID) -- 英勇打击
    if not spellID or spellID ~= 1464 then return end

    if heroicStrikeTimer then
        heroicStrikeTimer:Cancel()
        heroicStrikeTimer = nil
    end

    local overrideSpellID = C_Spell.GetOverrideSpell(1464)
    if overrideSpellID == 1269383 then
        local remaining = 15
        state.heroicStrike = remaining / 255
        self:UpdateStateBlock("状态", "英勇打击")

        heroicStrikeTimer = C_Timer.NewTicker(1, function()
            remaining = remaining - 1
            state.heroicStrike = remaining > 0 and (remaining / 255) or 0
            self:UpdateStateBlock("状态", "英勇打击")
            if remaining <= 0 then
                heroicStrikeTimer = nil
            end
        end, 15)
    else
        state.heroicStrike = 0
        self:UpdateStateBlock("状态", "英勇打击")
    end
end

function Fuyutsui:UpdateRune()
    local total = 0
    for i = 1, 6 do
        local runeCount = GetRuneCount(i)
        if runeCount then
            total = total + runeCount
        end
    end
    state.runeCount = total / 255 or 0
    self:UpdateBareStateBlock("符文", { "能量", "状态" })
end

function Fuyutsui:UpdateShapeshiftForm()
    local shapeshiftFormID = GetShapeshiftFormID() or 0
    state.shapeshiftFormID = shapeshiftFormID / 255
    self:UpdateStateBlock("状态", "姿态")
end

function Fuyutsui:UpdateDrinkStatus(spellID)
    local name = C_Spell.GetSpellName(spellID)
    if name == "饮水" or name == "进食饮水" then
        state.drinkStatus = true
        self:UpdatePlayerValid()
        if drinkStatusTimer then
            drinkStatusTimer:Cancel()
            drinkStatusTimer = nil
        end
        drinkStatusTimer = C_Timer.NewTimer(20, function()
            state.drinkStatus = false
            self:UpdatePlayerValid()
            drinkStatusTimer = nil
        end)
    else
        if drinkStatusTimer then
            drinkStatusTimer:Cancel()
            drinkStatusTimer = nil
        end
        state.drinkStatus = false
        self:UpdatePlayerValid()
    end
end

-- 死亡骑士天启骑士检测
local ActiveKnightSpells = {
    [454393] = 1,
    [454389] = 2,
    [454392] = 3,
    [454390] = 4,
}
local InactiveKnightSpells = {
    [444248] = 1,
    [444251] = 2,
    [444252] = 3,
    [444254] = 4,
}
local ActiveKnights = { false, false, false, false }

function Fuyutsui:UpdateKnightStatus(spellID)
    if ActiveKnightSpells[spellID] then
        ActiveKnights[ActiveKnightSpells[spellID]] = true
    end
    if InactiveKnightSpells[spellID] then
        ActiveKnights[InactiveKnightSpells[spellID]] = false
    end
end

local function GetActiveKnightsCount()
    local count = 0
    for i = 1, 4 do
        if ActiveKnights[i] then
            count = count + 1
        end
    end
    return count
end

function Fuyutsui:UpdateKnightStatusCount()
    state.knightCount = GetActiveKnightsCount() / 255
    self:UpdateStateBlock("状态", "天启骑士数量")
end

function Fuyutsui:HookChatFrameEditBox()
    for i = 1, NUM_CHAT_WINDOWS do
        local editBox = _G["ChatFrame" .. i .. "EditBox"]
        if editBox then
            editBox:HookScript("OnEditFocusGained", function()
                state.isChatOpen = true
                self:UpdatePlayerValid()
            end)
            editBox:HookScript("OnEditFocusLost", function()
                state.isChatOpen = false
                self:UpdatePlayerValid()
            end)
        end
    end
end

local mounts = {}
local mountCastingTimer = nil

function Fuyutsui:GetMountsInfo()
    wipe(mounts)
    local mountIDs = C_MountJournal.GetMountIDs()
    for i = 1, #mountIDs do
        local _, spellID, _, _, _, _, _, _, _, _, isCollected = C_MountJournal.GetMountInfoByID(mountIDs[i])
        if isCollected and spellID then
            mounts[spellID] = true
        end
    end
end

function Fuyutsui:UpdateMountCasting(spellID, casting)
    if casting then
        if spellID and not issecretvalue(spellID) and mounts[spellID] then
            if mountCastingTimer then
                mountCastingTimer:Cancel()
                mountCastingTimer = nil
            end
            state.mountCasting = true
            self:UpdatePlayerValid()
        end
    elseif state.mountCasting then
        if mountCastingTimer then
            mountCastingTimer:Cancel()
        end
        mountCastingTimer = C_Timer.NewTimer(0.1, function()
            state.mountCasting = false
            self:UpdatePlayerValid()
            mountCastingTimer = nil
        end)
    end
end

local forbearanceTimer = nil

function Fuyutsui:UpdatePlayerForbearance() -- 25771 自律
    if forbearanceTimer then
        forbearanceTimer:Cancel()
        forbearanceTimer = nil
    end

    local remaining = 30
    state.forbearance = remaining / 255
    self:UpdateStateBlock("状态", "自律")

    forbearanceTimer = C_Timer.NewTicker(1, function()
        remaining = remaining - 1
        state.forbearance = remaining > 0 and (remaining / 255) or 0
        self:UpdateStateBlock("状态", "自律")
        if remaining <= 0 then
            forbearanceTimer = nil
        end
    end, 30)
end
