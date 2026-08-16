using System;

namespace BrickVerse.Attributes;

/// <summary>Marks a read or method as safe to invoke from desynchronized Actor execution.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
public sealed class ParallelSafeAttribute : Attribute;
