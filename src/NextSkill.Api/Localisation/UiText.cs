namespace NextSkill.Api.Localisation;

/// <summary>
/// Marker type for NEXTSKILL-owned interface text.
///
/// The neutral resource is Polish (decision 051). Locale-specific resources
/// contain overrides only; missing translations fall back through .NET's
/// resource manager without application fallback code.
/// </summary>
public sealed class UiText;
