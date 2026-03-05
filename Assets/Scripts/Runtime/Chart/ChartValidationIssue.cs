namespace KalBeat.Charting
{
    public readonly struct ChartValidationIssue
    {
        public ChartValidationIssue(
            ChartValidationSeverity severity,
            string code,
            string message,
            int eventIndex = -1)
        {
            Severity = severity;
            Code = code;
            Message = message;
            EventIndex = eventIndex;
        }

        public ChartValidationSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public int EventIndex { get; }
    }
}
