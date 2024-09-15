namespace Jellyfin.Plugin.MediaAnalyzer;

/// <summary>
/// Analyzer Type.
/// </summary>
public enum AnalyzerType
{
    /// <summary>
    /// No Analyzer.
    /// </summary>
    NotSet,

    /// <summary>
    /// Blackframe Analyzer.
    /// </summary>
    BlackFrameAnalyzer,

    /// <summary>
    /// Chapter Analyzer.
    /// </summary>
    ChapterAnalyzer,

    /// <summary>
    /// Chromaprint Analyzer.
    /// </summary>
    ChromaprintAnalyzer,
}
