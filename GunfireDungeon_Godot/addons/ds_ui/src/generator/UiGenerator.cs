#if TOOLS

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Godot;

namespace DsUi.Generator
{

    /// <summary>
    /// Ui类生成器
    /// </summary>
    public static class UiGenerator
    {
        private static Dictionary<string, int> _nodeNameMap = new Dictionary<string, int>();
        private static int _nestedIndex = 1;

        /// <summary>
        /// 根据名称在编辑器中创建Ui, open 表示创建完成后是否在编辑器中打开这个ui
        /// </summary>
        public static bool CreateUi(string uiPath, bool open = false)
        {
            try
            {
                //创建脚本代码
                var index = uiPath.LastIndexOf("/", StringComparison.Ordinal) + 1;
                var uiName = uiPath.Substring(index);
                var subPath = uiPath.Substring(0, index);
                var nameSpace = subPath.Replace("/", ".");
                var uiNameFiledName = UiGeneratorUtils.FirstToUpper(subPath.Replace("/", "_")) + uiName;
                
                var scriptPath = DsUiConfig.UiCodeDir + subPath + UiGeneratorUtils.FirstToLower(uiName);
                var scriptFile = scriptPath + "/" + uiName + "Panel.cs";
                var scriptCode = $"using Godot;\n" +
                                 $"\n" +
                                 $"namespace {DsUiConfig.UiNamespace}.{nameSpace}{uiName};\n" +
                                 $"\n" +
                                 $"public partial class {uiName}Panel : {uiName}\n" +
                                 $"{{\n" +
                                 $"\n" +
                                 $"    public override void OnCreateUi()\n" +
                                 $"    {{\n" +
                                 $"        \n" +
                                 $"    }}\n" +
                                 $"\n" +
                                 $"    public override void OnDestroyUi()\n" +
                                 $"    {{\n" +
                                 $"        \n" +
                                 $"    }}\n" +
                                 $"\n" +
                                 $"}}\n";
                if (!Directory.Exists(scriptPath))
                {
                    Directory.CreateDirectory(scriptPath);
                }

                File.WriteAllText(scriptFile, scriptCode);

                //加载脚本资源
                var scriptRes = GD.Load<CSharpScript>("res://" + scriptFile);

                //创建场景资源
                var prefabPath = DsUiConfig.UiPrefabDir + subPath;
                var prefabFile = prefabPath + uiName + ".tscn";
                var prefabResPath = "res://" + prefabFile;
                if (!Directory.Exists(prefabPath))
                {
                    Directory.CreateDirectory(prefabPath);
                }

                var uiNode = new Control();
                uiNode.Name = uiName;
                uiNode.SetAnchorsPreset(Control.LayoutPreset.FullRect, true);
                uiNode.SetScript(scriptRes);
                var scene = new PackedScene();
                scene.Pack(uiNode);
                ResourceSaver.Save(scene, prefabResPath);

                //生成Ui结构代码
                GenerateUiCode(uiNode, uiNameFiledName, scriptPath + "/" + uiName + ".cs", nameSpace);

                //生成 ResourcePath.cs 代码
                //ResourcePathGenerator.Generate();

                //生成 UiManager_Methods.cs 代码
                UiManagerMethodsGenerator.Generate();

                //打开ui
                if (open)
                {
                    ds_ui.EditorInterface.OpenSceneFromPath(prefabResPath);
                }

            }
            catch (Exception e)
            {
                GD.PrintErr(e.ToString());
                return false;
            }

            return true;
        }

        /// <summary>
        /// 根据指定ui节点生成相应的Ui类, 并保存到指定路径下
        /// </summary>
        public static void GenerateUiCode(Node control, string uiNameFiledName, string path, string nameSpace)
        {
            _nodeNameMap.Clear();
            _nestedIndex = 1;
            var uiNode = EachNodeFromEditor(uiNameFiledName, control.Name, control);
            var code = GenerateClassCode(uiNode, nameSpace);
            File.WriteAllText(path, code);
        }
        
        /// <summary>
        /// 从编辑器中生成所有ui代码
        /// </summary>
        public static bool GenerateAllUiCode()
        {
            return GenEachUiPrefab(new DirectoryInfo(DsUiConfig.UiPrefabDir), GenerateUiCodeFromEditor);
        }

        /// <summary>
        /// 从编辑器中生成ui代码
        /// </summary>
        public static bool GenerateUiCodeFromEditor(Node control)
        {
            try
            {
                _nodeNameMap.Clear();
                _nestedIndex = 1;
                
                var resourcePath = control.GetScript().As<CSharpScript>().ResourcePath;
                var index = resourcePath.LastIndexOf("/", StringComparison.Ordinal);
                var uiName = resourcePath.Substring(index + 1, resourcePath.Length - index - 8 - 1);

                var temp = "res://" + DsUiConfig.UiCodeDir;
                var subPath = resourcePath.Substring(temp.Length, resourcePath.Length - temp.Length - uiName.Length * 2 - 9);
                var nameSpace = subPath.Replace("/", ".");
                var uiNameFiledName = UiGeneratorUtils.FirstToUpper(subPath.Replace("/", "_")) + uiName;

                var codeFilePath = DsUiConfig.UiCodeDir + subPath + UiGeneratorUtils.FirstToLower(uiName) + "/" + uiName + ".cs";
                
                GD.Print("重新生成ui代码: " + codeFilePath);
                
                var uiNode = EachNodeFromEditor(uiNameFiledName, uiName, control);
                var code = GenerateClassCode(uiNode, nameSpace);

                foreach (var pair in _nodeNameMap)
                {
                    if (pair.Value > 1)
                    {
                        GD.Print($"检测到同名节点: '{pair.Key}', 使用该名称的节点将无法生成唯一节点属性!");
                    }
                }

                File.WriteAllText(codeFilePath, code);
            }
            catch (Exception e)
            {
                GD.PrintErr(e.ToString());
                return false;
            }

            return true;
        }

        private static string GenerateClassCode(UiNodeInfo uiNodeInfo, string nameSpace)
        {
            var retraction = "    ";
            return $"using DsUi;\n\n" +
                   $"namespace {DsUiConfig.UiNamespace}.{nameSpace}{uiNodeInfo.OriginName};\n\n" +
                   $"/// <summary>\n" +
                   $"/// Ui代码, 该类是根据ui场景自动生成的, 请不要手动编辑该类, 以免造成代码丢失\n" +
                   $"/// </summary>\n" +
                   $"public abstract partial class {uiNodeInfo.OriginName} : UiBase\n" +
                   $"{{\n" +
                   GeneratePropertyListClassCode("", uiNodeInfo.OriginName + ".", uiNodeInfo, retraction) +
                   $"\n" +
                   $"    public {uiNodeInfo.OriginName}() : base(UiManager.UiName.{uiNodeInfo.UiNameFiledName})\n" +
                   $"    {{\n" +
                   $"    }}\n" +
                   $"\n" +
                   $"    public sealed override void OnInitNestedUi()\n" +
                   $"    {{\n" +
                   GenerateUiScriptCode("", uiNodeInfo, retraction) +
                   $"\n" +
                   GenerateInitNestedUi("", uiNodeInfo, retraction) +
                   $"    }}\n" +
                   $"\n" +
                   GenerateAllChildrenClassCode(uiNodeInfo.OriginName + ".", uiNodeInfo, retraction) +
                   $"\n" +
                   GenerateSoleLayerCode(uiNodeInfo, "", uiNodeInfo.OriginName, retraction) +
                   $"}}\n";
        }

        private static string GenerateUiScriptCode(string parent, UiNodeInfo uiNodeInfo, string retraction)
        {
            var str = "";
            var node = parent + (string.IsNullOrEmpty(parent) ? "" : ".") + uiNodeInfo.Name;
            if (uiNodeInfo.IsNodeScript)
            {
                str += retraction + $"    _ = {node};\n";
            }

            if (uiNodeInfo.Children != null)
            {
                for (var i = 0; i < uiNodeInfo.Children.Count; i++)
                {
                    var item = uiNodeInfo.Children[i];
                    if (uiNodeInfo.OriginName == uiNodeInfo.UiRootName)
                    {
                        str += GenerateUiScriptCode("", item, retraction);
                    }
                    else
                    {
                        str += GenerateUiScriptCode(node, item, retraction);
                    }
                }
            }

            return str;
        }

        private static string GenerateInitNestedUi(string parent, UiNodeInfo uiNodeInfo, string retraction)
        {
            var str = "";
            if (uiNodeInfo.IsRefUi)
            {
                var parentUi = "inst" + _nestedIndex++;
                var uiNode = $"{parentUi}.{uiNodeInfo.Name}.Instance";
                var parentNode = string.IsNullOrEmpty(parent) ? "this" : parent;
                var parentNode2 = string.IsNullOrEmpty(parent) ? "null" : parentUi;
                str += retraction + $"    var {parentUi} = {parentNode};\n";
                str += retraction + $"    RecordNestedUi({uiNode}, {parentNode2}, UiManager.RecordType.Open);\n";
                str += retraction + $"    {uiNode}.OnCreateUi();\n";
                str += retraction + $"    {uiNode}.OnInitNestedUi();\n\n";
            }
            else
            {
                if (uiNodeInfo.Children != null)
                {
                    for (var i = 0; i < uiNodeInfo.Children.Count; i++)
                    {
                        var item = uiNodeInfo.Children[i];
                        if (uiNodeInfo.OriginName == uiNodeInfo.UiRootName)
                        {
                            str += GenerateInitNestedUi("", item, retraction);
                        }
                        else
                        {
                            str += GenerateInitNestedUi(
                                parent + (string.IsNullOrEmpty(parent) ? "" : ".") + uiNodeInfo.Name, item, retraction);
                        }
                    }
                }
            }

            return str;
        }

        private static string GenerateAllChildrenClassCode(string parent, UiNodeInfo uiNodeInfo, string retraction)
        {
            var str = "";
            if (uiNodeInfo.Children != null)
            {
                for (var i = 0; i < uiNodeInfo.Children.Count; i++)
                {
                    var item = uiNodeInfo.Children[i];
                    str += GenerateAllChildrenClassCode(parent + item.OriginName + ".", item, retraction);
                    str += GenerateChildrenClassCode(parent, item, retraction);
                }
            }

            return str;
        }

        private static string GenerateChildrenClassCode(string parent, UiNodeInfo uiNodeInfo, string retraction)
        {
            string cloneCode;

            if (uiNodeInfo.IsRefUi)
            {
                cloneCode = retraction + $"    public override {uiNodeInfo.ClassName} Clone()\n";
                cloneCode += retraction + $"    {{\n";
                cloneCode += retraction +
                             $"        var uiNode = new {uiNodeInfo.ClassName}(UiPanel, ({uiNodeInfo.NodeTypeName})Instance.Duplicate());\n";
                cloneCode += retraction +
                             $"        UiPanel.RecordNestedUi(uiNode.Instance, this, UiManager.RecordType.Open);\n";
                cloneCode += retraction + $"        uiNode.Instance.OnCreateUi();\n";
                cloneCode += retraction + $"        uiNode.Instance.OnInitNestedUi();\n";
                cloneCode += retraction + $"        return uiNode;\n";
                cloneCode += retraction + $"    }}\n";
            }
            else
            {
                cloneCode = retraction +
                            $"    public override {uiNodeInfo.ClassName} Clone() => new (UiPanel, ({uiNodeInfo.NodeTypeName})Instance.Duplicate());\n";
            }

            return retraction + $"/// <summary>\n" +
                   retraction +
                   $"/// 路径: {parent}{uiNodeInfo.OriginName}\n" +
                   retraction + $"/// </summary>\n" +
                   retraction +
                   $"public class {uiNodeInfo.ClassName} : UiNode<{uiNodeInfo.UiRootName}Panel, {uiNodeInfo.NodeTypeName}, {uiNodeInfo.ClassName}>\n" +
                   retraction + $"{{\n" +
                   GeneratePropertyListClassCode("Instance.", parent, uiNodeInfo, retraction + "    ") +
                   retraction +
                   $"    public {uiNodeInfo.ClassName}({uiNodeInfo.UiRootName}Panel uiPanel, {uiNodeInfo.NodeTypeName} node) : base(uiPanel, node) {{  }}\n" +
                   cloneCode +
                   retraction + $"}}\n\n";
        }

        private static string GeneratePropertyListClassCode(string target, string parent, UiNodeInfo uiNodeInfo,
            string retraction)
        {
            var str = "";
            if (uiNodeInfo.Children != null)
            {
                for (var i = 0; i < uiNodeInfo.Children.Count; i++)
                {
                    var item = uiNodeInfo.Children[i];
                    str += GeneratePropertyCode(target, parent, item, retraction);
                }
            }

            return str;
        }

        private static string GeneratePropertyCode(string target, string parent, UiNodeInfo uiNodeInfo,
            string retraction)
        {
            string uiPanel;
            if (string.IsNullOrEmpty(target))
            {
                uiPanel = $"({uiNodeInfo.UiRootName}Panel)this";
            }
            else
            {
                uiPanel = "UiPanel";
            }

            return retraction + $"/// <summary>\n" +
                   retraction +
                   $"/// 节点路径: {parent}{uiNodeInfo.OriginName}\n" +
                   retraction + $"/// </summary>\n" +
                   retraction + $"public {uiNodeInfo.ClassName} {uiNodeInfo.Name}\n" +
                   retraction + $"{{\n" +
                   retraction + $"    get\n" +
                   retraction + $"    {{\n" +
                   retraction +
                   $"        if (_{uiNodeInfo.Name} == null) _{uiNodeInfo.Name} = new {uiNodeInfo.ClassName}({uiPanel}, {target}GetNode<{uiNodeInfo.NodeTypeName}>(\"{uiNodeInfo.OriginName}\"));\n" +
                   retraction + $"        return _{uiNodeInfo.Name};\n" +
                   retraction + $"    }}\n" +
                   retraction + $"}}\n" +
                   retraction + $"private {uiNodeInfo.ClassName} _{uiNodeInfo.Name};\n\n";
        }

        private static string GenerateSoleLayerCode(UiNodeInfo uiNodeInfo, string layerName, string parent,
            string retraction)
        {
            var str = "";
            if (uiNodeInfo.Children != null)
            {
                foreach (var nodeInfo in uiNodeInfo.Children)
                {
                    var layer = layerName;
                    if (layerName.Length > 0)
                    {
                        layer += ".";
                    }

                    layer += nodeInfo.Name;
                    var path = parent + "." + nodeInfo.OriginName;
                    str += GenerateSoleLayerCode(nodeInfo, layer, path, retraction);
                    if (IsSoleNameNode(nodeInfo))
                    {
                        str += $"{retraction}/// <summary>\n";
                        str +=
                            $"{retraction}/// 场景中唯一名称的节点, 节点路径: {path}\n";
                        str += $"{retraction}/// </summary>\n";
                        str += $"{retraction}public {nodeInfo.ClassName} S_{nodeInfo.OriginName} => {layer};\n\n";
                    }
                }
            }

            return str;
        }

        //返回指定节点在当前场景中是否是唯一名称的节点
        private static bool IsSoleNameNode(UiNodeInfo uiNodeInfo)
        {
            if (!_nodeNameMap.TryGetValue(uiNodeInfo.OriginName, out var count))
            {
                return true;
            }

            return count <= 1;
        }

        /// <summary>
        /// 在编辑器下递归解析节点, 由于编辑器下绑定用户脚本的节点无法直接判断节点类型, 那么就只能获取节点的脚本然后解析名称和命名空间
        /// </summary>
        private static UiNodeInfo EachNodeFromEditor(string uiNameFiledName, string uiRootName, Node node)
        {
            UiNodeInfo uiNode;
            //原名称
            var originName = Regex.Replace(node.Name, "[^\\w]", "");
            //字段名称
            var fieldName = "L_" + originName;
            //类定义该图层的类名
            string className;
            if (_nodeNameMap.ContainsKey(originName)) //有同名图层, 为了防止类名冲突, 需要在 UiNode 后面加上索引
            {
                var count = _nodeNameMap[originName];
                className = originName + "_" + count;
                _nodeNameMap[originName] = count + 1;
            }
            else
            {
                className = originName;
                _nodeNameMap.Add(originName, 1);
            }

            var script = node.GetScript().As<CSharpScript>();
            if (script == null) //无脚本, 说明是内置节点
            {
                uiNode = new UiNodeInfo(uiNameFiledName, uiRootName, fieldName, originName, className, node.GetType().FullName, false);
            }
            else //存在脚本
            {
                var index = script.ResourcePath.LastIndexOf("/", StringComparison.Ordinal);
                //文件名称
                var fileName = script.ResourcePath.Substring(index + 1, script.ResourcePath.Length - index - 3 - 1);
                //在源代码中寻找命名空间
                var match = Regex.Match(script.SourceCode, "(?<=namespace\\s+)[\\w\\.]+");
                bool isNodeScript;
                string nodeTypeName;
                if (match.Success) //存在命名空间
                {
                    nodeTypeName = match.Value + "." + fileName;
                }
                else //不存在命名空间
                {
                    nodeTypeName = fileName;
                }
                isNodeScript = CheckNodeScript(nodeTypeName);
                uiNode = new UiNodeInfo(uiNameFiledName, uiRootName, fieldName, originName, className, nodeTypeName, isNodeScript);

                //检测是否是引用Ui
                if (fileName.EndsWith("Panel"))
                {
                    var childUiName = fileName.Substring(0, fileName.Length - 5);
                    if (childUiName != uiRootName)
                    {
                        var i1 = nodeTypeName.IndexOf(".", StringComparison.Ordinal);
                        var i2 = nodeTypeName.LastIndexOf(".", StringComparison.Ordinal);
                    
                        if (i1 != -1 && i2 != -1 && i2 >= i1)
                        {
                            var tempPath = nodeTypeName.Substring(i1 + 1, i2 - i1 - 1).Replace(".", "/");
                            var resPath = DsUiConfig.UiPrefabDir + tempPath + ".tscn";
                            if (File.Exists(resPath))
                            {
                                //是引用Ui
                                uiNode.IsRefUi = true;
                                GD.Print("检测到引用其他Ui: " + resPath);
                            }
                        }
                    }
                }
            }

            //如果是引用Ui, 就没有必要递归子节点了
            if (uiNode.IsRefUi)
            {
                return uiNode;
            }

            var childCount = node.GetChildCount();
            if (childCount > 0)
            {
                for (var i = 0; i < childCount; i++)
                {
                    var children = node.GetChild(i);
                    if (children != null)
                    {
                        if (uiNode.Children == null)
                        {
                            uiNode.Children = new List<UiNodeInfo>();
                        }

                        uiNode.Children.Add(EachNodeFromEditor(uiNameFiledName, uiRootName, children));
                    }
                }
            }

            return uiNode;
        }

        private static bool CheckNodeScript(string typeName)
        {
            var type = typeof(UiGenerator).Assembly.GetType(typeName);
            if (type == null)
            {
                return false;
            }

            return type.IsAssignableTo(typeof(IUiNodeScript));
        }

        private static bool GenEachUiPrefab(DirectoryInfo directoryInfo, Func<Node, bool> cb)
        {
            var directoryInfos = directoryInfo.GetDirectories();
            foreach (var dir in directoryInfos)
            {
                if (!GenEachUiPrefab(dir, cb))
                {
                    return false;
                }
            }

            var fileInfos = directoryInfo.GetFiles();
            foreach (var fileInfo in fileInfos)
            {
                if (fileInfo.Extension == ".tscn") //文件
                {
                    var fullName = fileInfo.FullName.Replace("\\", "/");
                    var index = fullName.IndexOf(DsUiConfig.UiPrefabDir, StringComparison.Ordinal);
                    if (index == -1) continue;
                    // res://path/Name.tscn
                    var resPath = "res://" + fullName.Substring(index);
                    if (ResourceLoader.Exists(resPath))
                    {
                        var node = ResourceLoader.Load<PackedScene>(resPath).Instantiate();
                        try
                        {
                            if (UiGeneratorUtils.CheckIsUi(node))
                            {
                                GD.Print($"\n---------------------------------\n检测到Ui: {resPath}");
                                if (!cb(node))
                                {
                                    return false;
                                }
                            }
                        }
                        finally
                        {
                            node.QueueFree();
                        }
                    }
                }
            }

            return true;
        }

        private class UiNodeInfo
        {
            /// <summary>
            /// UiName 类中字段名称
            /// </summary>
            public string UiNameFiledName;
            
            /// <summary>
            /// 层级名称
            /// </summary>
            public string Name;

            /// <summary>
            /// 层级原名称
            /// </summary>
            public string OriginName;

            /// <summary>
            /// 层级类名
            /// </summary>
            public string ClassName;

            /// <summary>
            /// Godot节点类型名称
            /// </summary>
            public string NodeTypeName;

            /// <summary>
            /// 子节点
            /// </summary>
            public List<UiNodeInfo> Children;

            /// <summary>
            /// Ui根节点名称
            /// </summary>
            public string UiRootName;

            /// <summary>
            /// 是否实现了 IUiNodeScript 接口
            /// </summary>
            public bool IsNodeScript;

            /// <summary>
            /// 是否是引用Ui
            /// </summary>
            public bool IsRefUi;

            public UiNodeInfo(string uiNameFiledName, string uiRootName, string name, string originName, string className, string nodeTypeName,
                bool isNodeScript)
            {
                UiRootName = uiRootName;
                Name = name;
                OriginName = originName;
                ClassName = className;
                NodeTypeName = nodeTypeName;
                IsNodeScript = isNodeScript;
                UiNameFiledName = uiNameFiledName;
                if (isNodeScript)
                {
                    GD.Print("发现 IUiNodeScript 节点: " + originName);
                }
            }
        }
    }
}

#endif