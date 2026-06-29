using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace AncientWarfare3.attributes
{
    /// <summary>
    ///     通用夺舍标记(移植自 AW2)。给一个 mod 方法打上此特性,HarmonyTools.ReplaceMethods()
    ///     会用 Transpiler 把目标游戏方法的方法体重定向为“调用本方法 + return”。
    ///
    ///     关键:走 Harmony Patch,目标方法上别人挂的 Prefix/Postfix 仍正常执行,patch 链不破坏。
    ///
    ///     用法:
    ///     - [MethodReplace(typeof(Clan), "someMethod")] 显式指定目标类+名;
    ///     - [MethodReplace("someMethod")] 在目标类的子类里,按名找基类同名方法;
    ///     - [MethodReplace] 同名,自动找声明类的基类。
    /// </summary>
    public class MethodReplaceAttribute : Attribute
    {
        public readonly string MethodName;
        public readonly Type   TargetType;

        public MethodReplaceAttribute()
        {
        }

        public MethodReplaceAttribute(string pMethodName)
        {
            MethodName = pMethodName;
        }

        public MethodReplaceAttribute(Type pTargetType)
        {
            TargetType = pTargetType;
        }

        public MethodReplaceAttribute(Type pTargetType, string pMethodName)
        {
            TargetType = pTargetType;
            MethodName = pMethodName;
        }

        public MethodInfo TrackTargetMethod(MethodInfo pOnMethod)
        {
            List<Type> parameter_types = new();
            foreach (var parameter in pOnMethod.GetParameters())
            {
                parameter_types.Add(parameter.ParameterType);
            }

            List<Type> generic_types = new();
            foreach (var generic_type in pOnMethod.GetGenericArguments())
            {
                generic_types.Add(generic_type);
            }

            var parameter_types_array = parameter_types.ToArray();
            var generic_types_array = generic_types.Count == 0 ? null : generic_types.ToArray();

            Type target_type = TargetType;

            var target_method_name = string.IsNullOrEmpty(MethodName) ? pOnMethod.Name : MethodName;

            if (target_type != null)
                return TrackByTypeAndName(TargetType, target_method_name, parameter_types_array, generic_types_array);

            if (pOnMethod.DeclaringType == null) return null;

            target_type = pOnMethod.DeclaringType;
            if (target_type.BaseType != null)
                target_type = target_type.BaseType;

            MethodInfo target_method =
                TrackByTypeAndName(target_type, target_method_name, parameter_types_array, generic_types_array);
            while (target_method == null && target_type.BaseType != null)
            {
                target_type = target_type.BaseType;
                target_method =
                    TrackByTypeAndName(target_type, target_method_name, parameter_types_array, generic_types_array);
            }

            return target_method;
        }

        private MethodInfo TrackByTypeAndName(Type   pTargetType, string pMethodName, Type[] pParameterTypes,
                                              Type[] pGenericTypes)
        {
            if (pTargetType == null) return null;
            MethodInfo method = AccessTools.Method(pTargetType, pMethodName, pParameterTypes, pGenericTypes);
            if (method != null) return method;

            // 实例方法:替换方法第一个参数是 this(目标实例),去掉它再按目标方法实际参数匹配。
            if (pParameterTypes.Length == 0) return null;
            var list = pParameterTypes.ToList();
            list.RemoveAt(0);
            return AccessTools.Method(pTargetType, pMethodName, list.ToArray(), pGenericTypes);
        }
    }
}
