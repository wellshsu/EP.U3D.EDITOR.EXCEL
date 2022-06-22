//---------------------------------------------------------------------------//
//                    GNU GENERAL PUBLIC LICENSE                             //
//                       Version 2, June 1991                                //
//                                                                           //
// Copyright (C) Wells Hsu, wellshsu@outlook.com, All rights reserved.       //
// Everyone is permitted to copy and distribute verbatim copies              //
// of this license document, but changing it is not allowed.                 //
//                  SEE LICENSE.md FOR MORE DETAILS.                         //
//---------------------------------------------------------------------------//
using EP.U3D.EDITOR.BASE;
using Excel;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace EP.U3D.EDITOR.EXCEL
{
    public class BuildExcel
    {
        public static Type WorkerType = typeof(BuildExcel);

        [MenuItem(Constants.MENU_PATCH_BUILD_EXCEL)]
        public static void Invoke()
        {
            var worker = Activator.CreateInstance(WorkerType) as BuildExcel;
            worker.Process();
        }

        public virtual void Process()
        {
            List<string> ofiles = new List<string>();
#if EFRAME_CS
            Helper.CollectFiles(Constants.EXCEL_CS_PATH, ofiles, ".meta");
#endif
#if EFRAME_ILR
            Helper.CollectFiles(Constants.EXCEL_ILR_PATH, ofiles, ".meta");
#endif
#if EFRAME_LUA
            Helper.CollectFiles(Constants.EXCEL_LUA_PATH, ofiles, ".meta");
#endif
            Helper.CollectFiles(Constants.EXCEL_JSON_PATH, ofiles, ".meta");
            List<string> files = new List<string>();
            Helper.CollectFiles(Constants.EXCEL_SRC_PATH, files);
            List<string> nfiles = new List<string>();
            for (int i = 0; i < files.Count; i++)
            {
                string file = files[i];
                file = Helper.NormalizePath(file);
                if (file.EndsWith(".xlsx"))
                {
                    string delta = file.Replace(Constants.EXCEL_SRC_PATH, "");
                    delta = delta.LastIndexOf("/") > 0 ? delta.Substring(0, delta.LastIndexOf("/") + 1) : string.Empty;
                    string json = Excel2JSON(file, delta, Constants.EXCEL_JSON_PATH);
                    nfiles.Add(json);
                    string asset = json.Replace(Application.dataPath + "/", "");
#if EFRAME_CS
                    nfiles.Add(Excel2CS(file, delta, asset, Constants.EXCEL_CS_PATH, false));
#endif
#if EFRAME_ILR
                    nfiles.Add(Excel2CS(file, delta, asset, Constants.EXCEL_ILR_PATH, true));
#endif
#if EFRAME_LUA
                    nfiles.Add(Excel2LUA(file, delta, asset, Constants.EXCEL_LUA_PATH));
#endif
                }
            }
            // 清理不存在的
            for (int i = 0; i < ofiles.Count; i++)
            {
                var f = ofiles[i];
                bool valid = false;
                for (int j = 0; j < nfiles.Count; j++)
                {
                    var nf = nfiles[j];
                    if (nf == f)
                    {
                        valid = true;
                        break;
                    }
                }
                if (!valid)
                {
                    Helper.DeleteFile(f);
                    string dir = Path.GetDirectoryName(f);
                    List<string> tmp = new List<string>();
                    Helper.CollectFiles(dir, tmp, ".meta");
                    if (tmp.Count == 0)
                    {
                        Helper.DeleteDirectory(dir, true);
                    }
                }
            }
            AssetDatabase.Refresh();
            string toast = "Build excel done.";
            Helper.Log(toast);
            Helper.ShowToast(toast);
        }

        public virtual string Excel2CS(string src, string delta, string asset, string dst, bool ilr)
        {
            var pkg = Helper.FindPackage(Assembly.GetExecutingAssembly());
            var excel = new ExcelReader(src);
            if (excel.Sheet == null || excel.Sheet.Rows.Count < 4)
            {
                Helper.LogError("Excel2CS: invalid xlsx, struct must be row0-meta, row1-field, row2-type, row3-comment, {0}", src);
                return string.Empty;
            }
            object[] metas = excel.Sheet.Rows[0].ItemArray;
            object[] fields = excel.Sheet.Rows[1].ItemArray;
            object[] types = excel.Sheet.Rows[2].ItemArray;
            object[] comments = excel.Sheet.Rows[3].ItemArray;
            string clazz = (string)metas[1];
            string baseclazz = (string)metas[3];
            if (string.IsNullOrEmpty(baseclazz)) baseclazz = "object";
            bool keyaccess = (bool)metas[5];
            string template;
            if (Helper.HasFile(ilr ? Constants.EXCEL_ILR_TEMPLATE : Constants.EXCEL_CS_TEMPLATE))
            {
                template = Helper.OpenText(ilr ? Constants.EXCEL_ILR_TEMPLATE : Constants.EXCEL_CS_TEMPLATE);
            }
            else
            {
                template = Helper.OpenText(pkg.resolvedPath + (ilr ? "/Editor/Libs/Template~/Excel2ILR.txt" : "/Editor/Libs/Template~/Excel2CS.txt"));
            }
            string name = Path.GetFileNameWithoutExtension(src);
            string cs = Path.Combine(dst, delta, name + ".cs");
            string cfields = "";
            for (var i = 0; i < fields.Length; i++)
            {
                var field = fields[i] as string;
                var type = types[i] as string;
                var comment = comments[i] as string;
                cfields += $"\t\tprivate {type} _{field};\n";
                if (string.IsNullOrEmpty(comment) == false)
                {
                    cfields += $"\t\t// {comment}\n";
                }
                cfields += $"\t\tpublic {type} {field}() {{ return _{field}; }}\n";
            }
            string accessstr = "";
            if (keyaccess)
            {
                string ktype = types[0] as string;
                if (ktype == "string")
                {
                    string kfield = fields[0] as string;
                    for (var i = 4; i < excel.Sheet.Rows.Count; i++)
                    {
                        var row = excel.Sheet.Rows[i];
                        var kvalue = row[0] as string;
                        accessstr += $"\t\tprivate static {clazz} _{kvalue};\n";
                        accessstr += $"\t\tpublic static {clazz} {kvalue}() {{ if (_{kvalue} == null) _{kvalue} = READ(\"{kvalue}\", \"{kfield}\"); return _{kvalue}; }}\n";
                    }
                }
            }
            template = template.Replace("#CLASS#", clazz).Replace("#BASE_CLASS#", baseclazz).Replace("#ASSET_PATH#", asset).Replace("#FIELDS#", cfields).Replace("#KEYACCESS#", accessstr);
            Helper.SaveText(cs, template);
            return cs;
        }

        public virtual string Excel2LUA(string src, string delta, string asset, string dst)
        {
            var pkg = Helper.FindPackage(Assembly.GetExecutingAssembly());
            var excel = new ExcelReader(src);
            if (excel.Sheet == null || excel.Sheet.Rows.Count < 4)
            {
                Helper.LogError("Excel2LUA: invalid xlsx, struct must be row0-meta, row1-field, row2-type, row3-comment, {0}", src);
                return string.Empty;
            }
            object[] metas = excel.Sheet.Rows[0].ItemArray;
            object[] fields = excel.Sheet.Rows[1].ItemArray;
            object[] types = excel.Sheet.Rows[2].ItemArray;
            object[] comments = excel.Sheet.Rows[3].ItemArray;
            string clazz = (string)metas[1];
            bool keyaccess = (bool)metas[5];
            string template;
            if (Helper.HasFile(Constants.EXCEL_LUA_TEMPLATE))
            {
                template = Helper.OpenText(Constants.EXCEL_LUA_TEMPLATE);
            }
            else
            {
                template = Helper.OpenText(pkg.resolvedPath + "/Editor/Libs/Template~/Excel2LUA.txt");
            }
            string name = Path.GetFileNameWithoutExtension(src);
            string lua = Path.Combine(dst, delta, name + ".lua");
            string cfields = "";
            string cprops = "";
            for (var i = 0; i < fields.Length; i++)
            {
                var field = fields[i] as string;
                var type = types[i] as string;
                var comment = comments[i] as string;
                cfields += string.IsNullOrEmpty(comment) ? $"---@field _{field} {type}\n" : $"---@field _{field} {type} {comment}\n";
                cprops += string.IsNullOrEmpty(comment) ? $"---@return {type}\n" : $"--- {comment}\n---@return {type}\n";
                cprops += $"function {clazz}:{field}() return self._{field} end\n";
            }
            string accessstr = "";
            if (keyaccess)
            {
                string ktype = types[0] as string;
                if (ktype == "string")
                {
                    string kfield = fields[0] as string;
                    for (var i = 4; i < excel.Sheet.Rows.Count; i++)
                    {
                        var row = excel.Sheet.Rows[i];
                        var kvalue = row[0] as string;
                        accessstr += $"local _{kvalue}\n";
                        accessstr += $"---@return {clazz}\n";
                        accessstr += $"function {clazz}.{kvalue}() if _{kvalue} == nil then _{kvalue} = {clazz}.READ(\"{kvalue}\", \"{kfield}\") end return _{kvalue} end\n";
                    }
                }
            }
            template = template.Replace("#CLASS#", clazz).Replace("#ASSET_PATH#", asset).Replace("#FIELDS#", cfields).Replace("#PROPERTY#", cprops).Replace("#KEYACCESS#", accessstr);
            Helper.SaveText(lua, template);
            return lua;
        }

        public virtual string Excel2JSON(string src, string delta, string dst)
        {
            var excel = new ExcelReader(src);
            if (excel.Sheet == null || excel.Sheet.Rows.Count < 4)
            {
                Helper.LogError("Excel2JSON: invalid xlsx, struct must be row0-meta, row1-field, row2-type, row3-comment, {0}", src);
                return string.Empty;
            }
            object[] fields = excel.Sheet.Rows[1].ItemArray;
            object[] types = excel.Sheet.Rows[2].ItemArray;
            string name = Path.GetFileNameWithoutExtension(src);
            string json = Helper.NormalizePath($"{dst}{delta}{name}.json");
            string fstr = "";
            fstr += "[";
            for (var i = 4; i < excel.Sheet.Rows.Count; i++)
            {
                fstr += "{";
                var row = excel.Sheet.Rows[i];
                for (int j = 0; j < fields.Length; j++)
                {
                    var field = fields[j] as string;
                    var type = types[j] as string;
                    var value = row[j];
                    if (type == "bool")
                    {
                        value = row[j].ToString().ToLower();
                    }
                    else if (type == "string")
                    {
                        value = $"\"{value}\"".Trim();
                    }
                    fstr += $"\"_{field}\":{value}";
                    if (j < fields.Length - 1) fstr += ",";
                }
                fstr += "}";
                if (i < excel.Sheet.Rows.Count - 1) fstr += ",";
            }
            fstr += "]";
            Helper.SaveText(json, fstr);
            return json;
        }

        public class ExcelReader
        {
            public DataTable Sheet = null;

            public ExcelReader(string file)
            {
                FileStream mStream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                IExcelDataReader mExcelReader = ExcelReaderFactory.CreateOpenXmlReader(mStream);
                var mResultSet = mExcelReader.AsDataSet();
                if (mResultSet.Tables.Count > 0) Sheet = mResultSet.Tables[0];
            }
        }
    }
}