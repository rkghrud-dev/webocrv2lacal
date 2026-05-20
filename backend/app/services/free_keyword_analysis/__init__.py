"""Free local OCR keyword analysis package.

This package intentionally avoids Google Vision/API calls. It reads downloaded
image folders, creates an intermediate Excel analysis, and generates keyword
candidate lines from the local evidence.
"""

from .analyzer import AnalysisConfig, analyze_image_root

__all__ = ["AnalysisConfig", "analyze_image_root"]
