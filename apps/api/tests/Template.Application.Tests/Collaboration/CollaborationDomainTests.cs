using Template.Domain.Collaboration;

namespace Template.Application.Tests.Collaboration;

public sealed class CollaborationDomainTests
{
    [Theory]
    [InlineData(" Design ", "Design")]
    [InlineData("Команда_1", "Команда_1")]
    public void Team_names_are_trimmed_and_unicode_safe(string input, string expected)
    {
        Assert.True(TeamName.TryCreate(input, out var name));
        Assert.Equal(expected, name.Value);
        Assert.Equal(expected, name.ToString());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("name\nother", false)]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz", false)]
    public void Invalid_team_names_are_rejected(string input, bool expected) =>
        Assert.Equal(expected, TeamName.TryCreate(input, out _));

    [Fact]
    public void Team_names_accept_supplementary_plane_letters_and_digits()
    {
        const string nameWithAstralScalars = "\U00010400 \U0001D7CE";

        Assert.True(TeamName.TryCreate(nameWithAstralScalars, out var name));
        Assert.Equal(nameWithAstralScalars, name.Value);
    }

    [Fact]
    public void Team_name_length_is_counted_in_Unicode_scalars()
    {
        var fiftyAstralLetters = string.Concat(
            Enumerable.Repeat("\U00010400", TeamName.MaximumLength));

        Assert.True(TeamName.TryCreate(fiftyAstralLetters, out var name));
        Assert.Equal(fiftyAstralLetters, name.Value);
    }

    [Fact]
    public void Team_names_over_fifty_Unicode_scalars_are_rejected()
    {
        var fiftyOneAstralLetters = string.Concat(
            Enumerable.Repeat("\U00010400", TeamName.MaximumLength + 1));

        Assert.False(TeamName.TryCreate(fiftyOneAstralLetters, out _));
    }

    [Fact]
    public void Team_names_with_unpaired_surrogates_are_rejected() =>
        Assert.False(TeamName.TryCreate("\uD800", out _));

    [Fact]
    public void Collaboration_identifiers_follow_their_UUID_generation_policies()
    {
        var now = DateTimeOffset.Parse("2026-08-03T00:00:00Z");

        Assert.Equal(7, TeamId.New(now).Value.Version);
        Assert.Equal(7, TeamMemberId.New(now).Value.Version);
        Assert.Equal(4, InvitationId.New().Value.Version);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("accepted")]
    [InlineData("rejected")]
    [InlineData("canceled")]
    public void Invitation_stored_statuses_are_closed_and_canonical(string value)
    {
        Assert.True(InvitationStatus.TryParse(value, out var status));
        Assert.Equal(value, status.Value);
        Assert.Equal(value, status.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("expired")]
    [InlineData("Pending")]
    public void Unknown_invitation_stored_statuses_are_rejected(string? value) =>
        Assert.False(InvitationStatus.TryParse(value, out _));

    [Fact]
    public void Pending_invitation_at_expiry_is_displayed_as_expired() =>
        Assert.Equal(
            InvitationDisplayState.Expired,
            InvitationPolicy.GetDisplayState(
                InvitationStatus.Pending,
                DateTimeOffset.Parse("2026-08-03T00:00:00Z"),
                DateTimeOffset.Parse("2026-08-03T00:00:00Z")));

    [Fact]
    public void Non_pending_invitation_states_are_displayed_without_expiry_override()
    {
        var expired = DateTimeOffset.Parse("2026-08-03T00:00:00Z");
        var later = expired.AddTicks(1);

        Assert.Equal(InvitationDisplayState.Accepted,
            InvitationPolicy.GetDisplayState(InvitationStatus.Accepted, expired, later));
        Assert.Equal(InvitationDisplayState.Rejected,
            InvitationPolicy.GetDisplayState(InvitationStatus.Rejected, expired, later));
        Assert.Equal(InvitationDisplayState.Canceled,
            InvitationPolicy.GetDisplayState(InvitationStatus.Canceled, expired, later));
    }

    [Fact]
    public void Only_unexpired_pending_invitations_can_be_accepted_or_rejected()
    {
        var now = DateTimeOffset.Parse("2026-08-03T00:00:00Z");

        Assert.True(InvitationPolicy.CanAccept(InvitationStatus.Pending, now.AddTicks(1), now));
        Assert.True(InvitationPolicy.CanReject(InvitationStatus.Pending, now.AddTicks(1), now));
        Assert.False(InvitationPolicy.CanAccept(InvitationStatus.Pending, now, now));
        Assert.False(InvitationPolicy.CanReject(InvitationStatus.Pending, now, now));
        Assert.False(InvitationPolicy.CanAccept(InvitationStatus.Accepted, now.AddTicks(1), now));
        Assert.False(InvitationPolicy.CanReject(InvitationStatus.Rejected, now.AddTicks(1), now));
    }
}
