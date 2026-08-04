using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace AncientWarfare3.utils
{
    internal static class ReflectionDelegateFactory
    {
        internal static TDelegate TryCreate<TDelegate>(MethodInfo pMethod)
            where TDelegate : Delegate
        {
            if (pMethod == null) return null;
            try
            {
                MethodInfo invoke = typeof(TDelegate).GetMethod("Invoke");
                ParameterInfo[] delegateParameters = invoke.GetParameters();
                ParameterInfo[] methodParameters = pMethod.GetParameters();
                int instanceOffset = pMethod.IsStatic ? 0 : 1;
                if (delegateParameters.Length !=
                    methodParameters.Length + instanceOffset) return null;

                var lambdaParameters = new List<ParameterExpression>();
                foreach (ParameterInfo parameter in delegateParameters)
                    lambdaParameters.Add(Expression.Parameter(
                        parameter.ParameterType, parameter.Name));

                Expression instance = null;
                if (!pMethod.IsStatic)
                {
                    Type declaringType = pMethod.DeclaringType;
                    if (declaringType == null) return null;
                    instance = Convert(lambdaParameters[0], declaringType);
                }

                var arguments = new Expression[methodParameters.Length];
                for (int i = 0; i < arguments.Length; i++)
                    arguments[i] = Convert(
                        lambdaParameters[i + instanceOffset],
                        methodParameters[i].ParameterType);

                MethodCallExpression call = pMethod.IsStatic
                    ? Expression.Call(pMethod, arguments)
                    : Expression.Call(instance, pMethod, arguments);
                Expression body;
                if (invoke.ReturnType == typeof(void))
                    body = pMethod.ReturnType == typeof(void)
                        ? (Expression)call
                        : Expression.Block(call, Expression.Empty());
                else
                    body = Convert(call, invoke.ReturnType);

                return Expression.Lambda<TDelegate>(body,
                    lambdaParameters).Compile();
            }
            catch
            {
                return null;
            }
        }

        private static Expression Convert(Expression pValue, Type pTarget)
        {
            return pValue.Type == pTarget
                ? pValue
                : Expression.Convert(pValue, pTarget);
        }
    }
}
