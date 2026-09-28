namespace MapperR.Core.Extensions;

internal static class Extensions
{
    extension<T>(IEnumerable<T> src)
    {
        internal void ForEach(Action<T> action)
        {
            foreach (var item in src ?? []) action?.Invoke(item);
        }
    }
}