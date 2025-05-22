using System.Linq.Expressions;

namespace Mtb.Mapper.Core;

/// <summary>
/// 
/// </summary>
/// <typeparam name="TSource"></typeparam>
/// <typeparam name="TTarget"></typeparam>
public class MappingConfig<TSource, TTarget>
{
    /// <summary>
    /// 
    /// </summary>
    public Dictionary<string, string> PropertyMappings { get; } = new();
    public Dictionary<string, Delegate> Converters { get; } = new();

    // Метод для маппинга с использованием лямбда-выражений
    public void MapProperty<TSourceProp, TTargetProp>(
        Expression<Func<TSource, TSourceProp>> sourceSelector,
        Expression<Func<TTarget, TTargetProp>> targetSelector,
        Func<TSourceProp, TTargetProp>? converter = null)
    {
        var sourceProperty = GetPropertyName(sourceSelector);
        var targetProperty = GetPropertyName(targetSelector);

        PropertyMappings[targetProperty] = sourceProperty;

        if (converter != null)
            Converters[targetProperty] = converter;
    }

    // Метод для извлечения имени свойства из лямбда-выражения
    private static string GetPropertyName<T, TProp>(Expression<Func<T, TProp>> expression)
    {
        if (expression.Body is MemberExpression memberExpression)
            return memberExpression.Member.Name;

        throw new ArgumentException("Expression must be a member expression.");
    }
}