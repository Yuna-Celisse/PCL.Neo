using System;

namespace PCL.Neo.Core.Minecraft.ResourceProject.Curseforge;

[Serializable]
public record CurseforgeHashes(
    string value,
    int algo);
