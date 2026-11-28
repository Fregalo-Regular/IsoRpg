namespace Game;

/// Clip — анимация каста; FaceTarget — доворачивать персонажа к точке прицеливания.
public sealed record AbilityDef(string Name, float Cd, float Range, string Clip, bool FaceTarget);