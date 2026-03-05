using System.Collections.Generic;

namespace KalBeat.Charting
{
    public sealed class ChartValidationResult
    {
        private readonly List<ChartValidationIssue> issues = new List<ChartValidationIssue>();

        public IReadOnlyList<ChartValidationIssue> Issues => issues;

        public bool HasError
        {
            get
            {
                for (int i = 0; i < issues.Count; i++)
                {
                    if (issues[i].Severity == ChartValidationSeverity.Error)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public int ErrorCount
        {
            get
            {
                int count = 0;

                for (int i = 0; i < issues.Count; i++)
                {
                    if (issues[i].Severity == ChartValidationSeverity.Error)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int WarningCount
        {
            get
            {
                int count = 0;

                for (int i = 0; i < issues.Count; i++)
                {
                    if (issues[i].Severity == ChartValidationSeverity.Warning)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public void AddError(string code, string message, int eventIndex = -1)
        {
            issues.Add(new ChartValidationIssue(ChartValidationSeverity.Error, code, message, eventIndex));
        }

        public void AddWarning(string code, string message, int eventIndex = -1)
        {
            issues.Add(new ChartValidationIssue(ChartValidationSeverity.Warning, code, message, eventIndex));
        }
    }
}
