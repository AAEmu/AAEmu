# GF-S10B portal follow-ups

This slice covers only the portal behaviour that is backed by the shipped content schema and the
portal packets already present in the tree.

## Audited surface

- The ledger's S10 remainder is the shared landing path plus OpenPortal, portal-book variants and
  private creation. The shared landing helper from the earlier teleport fix is reused; this slice does
  not introduce another landing implementation.
- `open_portal_effects` supplies the effect distance, enter/exit portal NPC templates and the faction
  permission flag. The effect id is carried by the skill effect template, so the manager can resolve
  those values instead of using a fixed NPC template.
- `open_portal_inland_reagents` and `open_portal_outland_reagents` are keyed by
  `open_portal_effect_id` and carry the shipped `priority` order. Reagents are now selected for the
  actual effect and consumed in that order.
- The private portal creation path uses the existing `SkillObjectUnk2` name/id shape. Creation is
  rejected before allocating an id when the name is empty or a coordinate is non-finite.

## Implemented

1. Thread the loaded `OpenPortalEffect` into `PortalManager.OpenPortal`.
2. Resolve enter/exit NPC templates from the effect row; a missing side is a logged refusal.
3. Filter inland/outland reagents by effect id and order by content priority, with row id as a stable
   tie-breaker.
4. Validate private portal names and finite coordinates; the GM registration command and the
   `SavePortal` effect share the same creation method. Names are bounded to 128 UTF-8 bytes for
   create and rename symmetry.
5. Register each live entrance/exit pair by owner and by the book entry it was opened from. The
   registry is keyed by the entry object, never by id: private book ids start at 4096 and
   `district_return_points.id` runs up to 4832, so one id can name a private and a district entry at
   the same time. `CSDeletePortal` resolves the entry from the book the wire type byte names — the
   private book or the district book — and removes only that entry's pair; the `portal.Type`
   return-point fallback is kept on the district lookup, which is the case it exists for. Orderly
   logout and hard disconnect remove all remaining pairs. A delete starts one call per pair member:
   `Portal.Delete()` already cascades to the linked portal, so a member the cascade already removed
   is skipped instead of receiving a second death packet. Liveness is read from a dedicated flag on
   the portal unit rather than from hp, which depends on the template's stat formula. The registry
   is guarded by a lock because the cast flow and the disconnect path both touch it.
6. Keep the existing shared landing path for walking through a created portal.
7. Refuse a portal use the character cannot reach. The object id in `CSUsePortal` is client supplied
   and resolves any open portal in the world, so with pairs living until logout a forged use could
   teleport the sender to any pair's destination. `PortalUseRules` requires the portal to be in the
   region neighbourhood the character can see the world from, the same authority that already guards
   the other client-named targets (`CSGetDoodadManikinSkin`, `CSDoodadQuestNotiPacket` and the static
   gimmick grasp). The guard runs before the ownership check, so a remote probe does not get an
   answer that depends on the portal's owner.

## Deliberately not claimed

- **Temporary portal lifetime / use cooldown:** the shipped `open_portal_effects` and reagent tables
  have no lifetime or cooldown column, and no additional authoritative field was found. The previous
  hardcoded 30-second `KillPortalTask` schedule was therefore removed; as a deliberate consequence,
  a successful cast no longer auto-expires its two live NPC units and chained casts can create more
  pairs. Owner deletion, CSDeletePortal and logout/disconnect now clean them up. A real expiry or
  use cooldown still needs a separately evidenced content or protocol source. The now-unreferenced
  `KillPortalTask` class was removed rather than left as a misleading dead expiry path.
- **Book-variant selection:** the `SavePortal` special-effect rows and `SkillObjectUnk2` shape do not
  establish a complete variant mapping for the value fields. Private creation and rename are wired;
  district/expedition variant selection is left to that missing evidence.
- **Faction-permission enforcement:** the flag is loaded and preserved as content, but its client
  semantics are not proven strongly enough to apply an authorization rule.
- **Portal use radius:** no shipped content provides a distance a character may walk a portal from.
  `open_portal_effects.distance` (3.0 on all 11 rows) is the radius the caster may place the portal
  inside — the open path already applies it — and the portal `npcs` rows carry no interaction range,
  no AI params and no interaction set. A contact distance also cannot be derived from the model: six
  of the eight portal models named by `open_portal_effects` (3038, 3039, 3201, 3202, 3243, 3244) have
  no `actor_models` row at all, so their `ModelSize` is 0 and a model-radius guard would refuse every
  legitimate use of those templates. The reachability guard therefore uses the region neighbourhood
  rather than a distance, and a true walk-in contact radius stays a follow-up that needs a content or
  protocol source.

## Verification

The focused tests cover effect-id reagent filtering/order, content NPC-pair validation, private portal
name bounds (ASCII and multibyte) and coordinate validation, a manager-level cast that resolves both
portal templates and links the pair, both directions of a private/district id collision (a district
request and its private mirror, each leaving the other entry's live pair intact), the district
return-point id fallback, and the real `Portal.Delete()` cascade showing that a logout starts exactly
one delete per pair. The full unit suite is run before publication.
