using System;

namespace PCL.Neo.Core.Minecraft.ResourceProject.Curseforge;

[Serializable]
public record CurseforgeAuthors(
    int id,
    string name,
    string url);
