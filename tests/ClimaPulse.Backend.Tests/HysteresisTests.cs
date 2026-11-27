using ClimaPulse.Models;
using ClimaPulse.Services;

namespace ClimaPulse.Backend.Tests;

/// <summary>
/// The event hysteresis rule (SRD FR-22 to FR-25, OPEN-1) through the pure
/// AnomalyDetectionService.DecideEvent. No database, no network (NFR-13).
/// </summary>
public class HysteresisTests
{
    private static AnomalyEvent OpenEvent(double peakZ) => new() { PeakZScore = peakZ };

    [Theory]
    [InlineData(2.0, EventAction.Open)]
    [InlineData(1.99, EventAction.Nothing)]
    [InlineData(-2.0, EventAction.Open)]       // cold works like hot
    [InlineData(-1.99, EventAction.Nothing)]
    public void Without_an_open_event_it_opens_at_2_0_and_not_below(double z, EventAction expected)
    {
        Assert.Equal(expected, AnomalyDetectionService.DecideEvent(null, z));
    }

    [Theory]
    [InlineData(2.3, 1.9, EventAction.Nothing)]
    [InlineData(2.3, 1.75, EventAction.Nothing)]
    [InlineData(2.3, 1.74, EventAction.Close)]
    [InlineData(-2.3, -1.9, EventAction.Nothing)]
    [InlineData(-2.3, -1.75, EventAction.Nothing)]
    [InlineData(-2.3, -1.74, EventAction.Close)]
    public void An_open_event_stays_open_down_to_1_75_and_closes_below(double peakZ, double z, EventAction expected)
    {
        Assert.Equal(expected, AnomalyDetectionService.DecideEvent(OpenEvent(peakZ), z));
    }

    [Fact]
    public void Ten_anomalous_readings_in_a_row_give_exactly_one_event()
    {
        double[] readings = [2.1, 2.4, 1.9, 2.0, 2.6, 1.8, 2.2, 3.1, 2.9, 2.0];
        AnomalyEvent? open = null;
        var opened = 0;

        foreach (var z in readings)
        {
            switch (AnomalyDetectionService.DecideEvent(open, z))
            {
                case EventAction.Open: opened++; open = OpenEvent(z); break;
                case EventAction.UpdatePeak: open!.PeakZScore = z; break;
                case EventAction.Close: open = null; break;
            }
        }

        Assert.Equal(1, opened);
        Assert.Equal(3.1, open!.PeakZScore);
    }

    [Fact]
    public void A_worse_reading_updates_the_peak_and_a_milder_one_never_lowers_the_tier()
    {
        Assert.Equal(EventAction.UpdatePeak, AnomalyDetectionService.DecideEvent(OpenEvent(3.5), 4.2));
        Assert.Equal(EventAction.UpdatePeak, AnomalyDetectionService.DecideEvent(OpenEvent(-3.5), -4.2));

        // Milder but still anomalous (Watch after Warning): nothing is written, so the stored tier stays.
        // The SQL in AnomalyEventRepository.UpdatePeak also never lowers it (database test).
        Assert.Equal(EventAction.Nothing, AnomalyDetectionService.DecideEvent(OpenEvent(3.5), 2.1));
        Assert.Equal(EventAction.Nothing, AnomalyDetectionService.DecideEvent(OpenEvent(3.5), 3.5));
    }

    [Fact]
    public void A_sign_flip_closes_the_event_and_the_next_decision_opens_a_new_one()
    {
        var hot = OpenEvent(2.8);

        Assert.Equal(EventAction.Close, AnomalyDetectionService.DecideEvent(hot, -2.5));
        Assert.Equal(EventAction.Open, AnomalyDetectionService.DecideEvent(null, -2.5));

        // A flip to a value that is not anomalous on its own closes without reopening.
        Assert.Equal(EventAction.Close, AnomalyDetectionService.DecideEvent(hot, -1.8));
        Assert.Equal(EventAction.Nothing, AnomalyDetectionService.DecideEvent(null, -1.8));
    }
}
