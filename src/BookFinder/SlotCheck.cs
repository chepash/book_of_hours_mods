using System;
using System.Linq;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.UI;

namespace BookFinder;

/// <summary>Which of a workstation's slots would take a card, judged by the game's own slot rules.</summary>
internal static class SlotCheck
{
    /// <summary>The slot whose Essential holds this aspect: "ability" (Soul), "skill", "memory".</summary>
    public static SphereSpec Slot(DeskEntry desk, string essential) =>
        desk.Situation?.Verb?.Slots?.FirstOrDefault(s => s.Essential != null && s.Essential.ContainsKey(essential));

    public static bool Takes(SphereSpec slot, ItemEntry item) =>
        slot != null && item?.Stack != null && slot.CheckPayloadAllowedHere(item.Stack).MatchType == SlotMatchForAspectsType.Okay;

    /// <summary>Any slot other than Soul / Skill / Memory (Papers, With, ...) takes it.</summary>
    public static bool OtherSlotTakes(DeskEntry desk, ItemEntry item) =>
        desk.Situation?.Verb?.Slots != null && desk.Situation.Verb.Slots.Any(s =>
            (s.Essential == null || !s.Essential.ContainsKey("ability") && !s.Essential.ContainsKey("skill") && !s.Essential.ContainsKey("memory"))
            && Takes(s, item));

    /// <summary>
    /// The card sits in a desk whose recipe is already running. A card only laid into an idle
    /// desk's slot, or waiting in a finished desk's results, is still free.
    /// </summary>
    public static bool InRunningRecipe(ItemEntry item)
    {
        if (item.HostToken == null || !item.HostToken.IsValid() || !(item.HostToken.Payload is Situation host))
            return false;
        try
        {
            StateEnum state = host.StateIdentifier;
            return state != StateEnum.Unstarted && state != StateEnum.Inchoate && state != StateEnum.Complete;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
