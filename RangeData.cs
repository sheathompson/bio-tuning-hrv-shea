public class RangeData
{
    public double Start { get; set; }
    public string Label { get; set; }

    public RangeData() { }

    public RangeData(double start, string label)
    {
        Start = start;
        Label = label;
    }
}