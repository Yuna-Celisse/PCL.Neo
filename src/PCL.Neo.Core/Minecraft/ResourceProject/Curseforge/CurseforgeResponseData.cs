using System;
using System.Collections.Generic;

namespace PCL.Neo.Core.Minecraft.ResourceProject.Curseforge;

[Serializable]
public record class CurseforgeProjectResponse(CurseforgeProject data);
[Serializable]
public record class CurseforgeProjectsResponse(List<CurseforgeProject> data);
