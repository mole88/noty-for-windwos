using Noty.Core;
using Noty.Deck;
using NUnit.Framework;

namespace Noty.Tests;

public sealed class DeckTests
{
    [Test]
    public void DeckState_exposes_only_an_expanded_note_id()
    {
        var expanded = DeckState.Expanded("note-1");

        Assert.Multiple(() =>
        {
            Assert.That(DeckState.Rest.ExpandedId, Is.Null);
            Assert.That(DeckState.Fan.ExpandedId, Is.Null);
            Assert.That(expanded.ExpandedId, Is.EqualTo("note-1"));
            Assert.That(expanded.ToString(), Is.EqualTo("expanded(note-1)"));
            Assert.That(expanded.Rank, Is.GreaterThan(DeckState.Fan.Rank));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(14)]
    [TestCase(30)]
    public void PillHeight_is_positive_and_bounded(int count)
    {
        var height = DeckGeom.PillHeight(count, 1.25);

        Assert.That(height, Is.GreaterThan(0));
        Assert.That(height, Is.LessThanOrEqualTo(DeckGeom.PillHeight(1000, 1.25)));
    }

    [Test]
    public void Compact_layout_uses_fixed_chip_spacing()
    {
        var layout = DeckGeom.Layout(800, 4, hasMore: true, DeckStyle.Compact);

        Assert.Multiple(() =>
        {
            Assert.That(layout.Count, Is.EqualTo(4));
            Assert.That(layout.ItemHeight, Is.EqualTo(DeckGeom.ChipHeight));
            Assert.That(layout.Pitch, Is.EqualTo(DeckGeom.ChipHeight + DeckGeom.ChipGap));
            Assert.That(layout.HasMore, Is.True);
        });
    }

    [Test]
    public void Tab_layout_shrinks_to_fit_a_short_panel()
    {
        var roomy = DeckGeom.Layout(1200, 5, false, DeckStyle.Tabs, longestLabel: 90);
        var shortPanel = DeckGeom.Layout(300, 5, false, DeckStyle.Tabs, longestLabel: 90);

        Assert.That(shortPanel.Pitch, Is.LessThan(roomy.Pitch));
    }

    [Test]
    public void Overflow_layout_keeps_readable_tab_pitch()
    {
        var layout = DeckGeom.Layout(300, 20, false, DeckStyle.Tabs,
                                     longestLabel: 70, allowOverflow: true);

        Assert.Multiple(() =>
        {
            Assert.That(layout.Pitch, Is.GreaterThanOrEqualTo(DeckGeom.PitchMin));
            Assert.That(layout.Overflows, Is.True);
        });
    }
}
