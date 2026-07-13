#if UNITY_EDITOR
// Asset imports must never mutate scenes or prefabs. Architecture migration and
// validation are explicit menu/CI operations so source-control diffs stay reviewable.
internal static class NetworkSceneSetupPostprocessor
{
}
#endif
