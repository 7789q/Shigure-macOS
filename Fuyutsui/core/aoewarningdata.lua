local addon, ns = ...

-- 手动配置普通 AOE 的 NPC ID + Spell ID 身份，不配置首次时间或循环 CD。
-- 格式：Fuyutsui.AOEWarningCasts["NPC_ID:SPELL_ID"] = true
Fuyutsui.AOEWarningCasts = Fuyutsui.AOEWarningCasts or {}

function Fuyutsui:IsConfiguredAOEWarningCast(npcID, spellID)
    if type(npcID) ~= "number" or type(spellID) ~= "number" then return false end
    return self.AOEWarningCasts[tostring(npcID) .. ":" .. tostring(spellID)] == true
end
