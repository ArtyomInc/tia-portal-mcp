using System;
using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public static class ProjectTreeNodeTypes
{
    public const string Device = "Device";
    public const string PlcSoftware = "PlcSoftware";
    public const string SoftwareUnit = "SoftwareUnit";
    public const string BlockFolder = "BlockFolder";
    public const string SystemBlockFolder = "SystemBlockFolder";
    public const string Ob = "OB";
    public const string Fb = "FB";
    public const string Fc = "FC";
    public const string GlobalDb = "GlobalDB";
    public const string InstanceDb = "InstanceDB";
    public const string ArrayDb = "ArrayDB";
    public const string Block = "Block";
    public const string TagTableFolder = "TagTableFolder";
    public const string TagTable = "TagTable";
    public const string TypeFolder = "TypeFolder";
    public const string Type = "Type";

    public static readonly IReadOnlyCollection<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Device, PlcSoftware, SoftwareUnit, BlockFolder, SystemBlockFolder,
        Ob, Fb, Fc, GlobalDb, InstanceDb, ArrayDb, Block,
        TagTableFolder, TagTable, TypeFolder, Type
    };
}
