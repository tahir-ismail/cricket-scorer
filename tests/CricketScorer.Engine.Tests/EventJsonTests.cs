namespace CricketScorer.Engine.Tests;

public class EventJsonTests
{
    // Builds one event of every type.
    private static MatchEvent[] OneOfEach()
    {
        Guid Id() => Guid.NewGuid();
        return
        [
            new InningsStarted(Id(), Id(), Id(), Id(), Id(), [Id(), Id()]),
            new BallBowled(Id(), 1, ExtraType.NoBall, 1, false, new Dismissal(DismissalType.RunOut, Id(), Id())),
            new BowlerChanged(Id(), Id()),
            new BatterIn(Id(), Id()),
            new StrikeSwapped(Id()),
            new BatterRetired(Id(), Id(), true),
            new PenaltyRuns(Id(), Id()),
            new OversChanged(Id(), 15),
            new TargetSet(Id(), 120),
            new InningsEnded(Id(), InningsEndReason.Declared),
            new ResultOverridden(Id(), ResultType.Draw, null, "Winning draw"),
            new MatchClosed(Id())
        ];
    }

    // Checks every event type round-trips with camelCase names and string enums.
    [Fact]
    public void EventsRoundTripWithWebOptions()
    {
        foreach (var e in OneOfEach())
        {
            var json = EventJson.Serialize(e);
            Assert.Equal(json, EventJson.Serialize(EventJson.Deserialize(json)));
        }
        var ball = EventJson.Serialize(new BallBowled(Guid.Empty, Extra: ExtraType.Wide, ExtraRuns: 1));
        Assert.Contains("\"type\":\"ball\"", ball);
        Assert.Contains("\"extra\":\"wide\"", ball);
        Assert.Contains("\"resultType\":\"draw\"", EventJson.Serialize(OneOfEach()[10]));
    }

    // Checks the "type" property can come after other properties.
    [Fact]
    public void EventWithTypeAfterOtherFieldsDeserializes()
    {
        var json = "{\"id\":\"8d3c1a52-6f0e-4c39-9a57-3f1b2b6a7c10\",\"batRuns\":4,\"boundary\":true,\"type\":\"ball\"}";
        var ball = Assert.IsType<BallBowled>(EventJson.Deserialize(json));
        Assert.Equal(4, ball.BatRuns);
        Assert.True(ball.Boundary);
    }

    // Checks an unknown event type is refused with a JSON error, not accepted as something else.
    [Fact]
    public void UnknownTypeThrowsJsonException() =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            EventJson.Deserialize("{\"type\":\"teleport\",\"id\":\"8d3c1a52-6f0e-4c39-9a57-3f1b2b6a7c10\"}"));
}
