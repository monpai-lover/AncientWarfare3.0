using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using AncientWarfare3.attributes;
using HarmonyLib;

namespace AncientWarfare3.utils
{
    /// <summary>
    ///     通用夺舍工具(移植自 AW2)。OnModLoad 时调 ReplaceMethods() 扫描本程序集所有
    ///     带 [MethodReplace] 的方法,用 Transpiler 把目标游戏方法体重定向为“调用替换方法 + return”。
    ///
    ///     原理:Harmony 对同一方法的 Prefix/Postfix/Transpiler 分层叠加。本工具只替换“方法体”那层,
    ///     不动 Prefix/Postfix 挂载 —— 别人对目标方法挂的 Prefix/Postfix 仍正常执行。
    /// </summary>
    internal static class HarmonyTools
    {
        private static MethodInfo _new_method;

        public static void ReplaceMethods()
        {
            Type[] types = Assembly.GetExecutingAssembly().GetTypes();

            HashSet<string> replaced_methods = new();
            foreach (Type type in types)
            {
                MethodInfo[] method_infos = type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                                            BindingFlags.Public   | BindingFlags.NonPublic);
                foreach (MethodInfo method_info in method_infos)
                {
                    var attribute = method_info.GetCustomAttribute<MethodReplaceAttribute>();
                    if (attribute == null) continue;

                    MethodInfo target_method = attribute.TrackTargetMethod(method_info);

                    if (target_method != null)
                    {
                        StringBuilder method_full_name = new();
                        method_full_name.Append(target_method.DeclaringType?.FullName);
                        method_full_name.Append(".");
                        method_full_name.Append(target_method.Name);
                        method_full_name.Append("<");
                        foreach (var parameter in target_method.GetParameters())
                        {
                            method_full_name.Append(parameter.ParameterType.FullName);
                            method_full_name.Append(",");
                        }

                        method_full_name.Append(">");

                        if (replaced_methods.Contains(method_full_name.ToString()))
                        {
                            ModClass.LogWarning("重复替换方法, 请检查 MethodReplaceAttribute 的参数是否正确");
                            ModClass.LogWarning($"Attribute 作用于: {method_info.DeclaringType?.FullName}:{method_info.Name}");
                            ModClass.LogWarning($"重复替换方法: {method_full_name}");
                            continue;
                        }

                        try
                        {
                            ReplaceMethod(method_info, target_method);
                            replaced_methods.Add(method_full_name.ToString());
                        }
                        catch (Exception e)
                        {
                            ModClass.LogWarning($"替换方法 {method_full_name} 时发生错误: {e}");
                            ModClass.LogWarning(e.StackTrace);
                        }
                    }
                    else
                    {
                        ModClass.LogWarning("无法找到目标替换方法, 请检查 MethodReplaceAttribute 的参数是否正确");
                        ModClass.LogWarning($"Attribute 作用于: {method_info.DeclaringType?.FullName}:{method_info.Name}");
                    }
                }
            }
        }

        private static void ReplaceMethod(MethodInfo pNewMethod, MethodInfo pTargetMethod)
        {
            _new_method = pNewMethod;
            Harmony harmony =
                new($"AncientWarfare3.AutoMethodReplaceTool.{pTargetMethod.DeclaringType?.FullName}.{pTargetMethod.Name}");
            harmony.Patch(pTargetMethod,
                          transpiler: new HarmonyMethod(typeof(HarmonyTools), nameof(_method_replace_patch)));
        }

        /// <summary>Transpiler:丢弃目标方法原 IL,生成“按参数转调替换方法 + return”。</summary>
        private static IEnumerable<CodeInstruction> _method_replace_patch(IEnumerable<CodeInstruction> instr)
        {
            var codes = new List<CodeInstruction>();

            var local_method = _new_method;
            var i = 0;
            if (!local_method.IsStatic)
            {
                codes.Add(new CodeInstruction(OpCodes.Ldarg,     i));
                codes.Add(new CodeInstruction(OpCodes.Castclass, local_method.DeclaringType));
                i++;
            }

            var param_count = local_method.GetParameters().Length + (local_method.IsStatic ? 0 : 1);
            for (; i < param_count; i++)
            {
                codes.Add(new CodeInstruction(OpCodes.Ldarg, i));
            }

            codes.Add(new CodeInstruction(OpCodes.Callvirt, local_method));
            codes.Add(new CodeInstruction(OpCodes.Ret));

            return codes;
        }
    }
}
