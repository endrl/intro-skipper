namespace Jellyfin.Plugin.MediaAnalyzer;

/// <summary>
/// Analyzer Type.
/// </summary>
public enum AnalyzerType
{
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
