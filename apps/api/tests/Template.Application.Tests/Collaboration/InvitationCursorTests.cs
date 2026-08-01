using Template.Application.Collaboration;
using Template.Domain.Collaboration;

namespace Template.Application.Tests.Collaboration;

public sealed class InvitationCursorTests
{
    [Fact]
    public void Organization_invitation_cursor_round_trips_a_utc_position()
    {
        var position = new OrganizationInvitationCursorPosition(
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            new InvitationId(Guid.Parse("00000000-0000-0000-0000-000000000001")));

        var encoded = InvitationCursor.Encode(position);

        Assert.True(InvitationCursor.TryDecode(encoded, out OrganizationInvitationCursorPosition decoded));
        Assert.Equal(position, decoded);
    }

    [Fact]
    public void Account_invitation_cursor_round_trips_a_utc_position()
    {
        var position = new AccountInvitationCursorPosition(
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            new InvitationId(Guid.Parse("00000000-0000-0000-0000-000000000001")));

        var encoded = InvitationCursor.Encode(position);

        Assert.True(InvitationCursor.TryDecode(encoded, out AccountInvitationCursorPosition decoded));
        Assert.Equal(position, decoded);
    }

    [Fact]
    public void Invitation_cursor_kinds_are_not_interchangeable()
    {
        var organization = InvitationCursor.Encode(new OrganizationInvitationCursorPosition(
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            new InvitationId(Guid.Parse("00000000-0000-0000-0000-000000000001"))));
        var account = InvitationCursor.Encode(new AccountInvitationCursorPosition(
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            new InvitationId(Guid.Parse("00000000-0000-0000-0000-000000000001"))));

        Assert.False(InvitationCursor.TryDecode(organization, out AccountInvitationCursorPosition _));
        Assert.False(InvitationCursor.TryDecode(account, out OrganizationInvitationCursorPosition _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("broken")]
    [InlineData("abcd=")]
    public void Invitation_cursors_reject_malformed_values(string value)
    {
        Assert.False(InvitationCursor.TryDecode(value, out OrganizationInvitationCursorPosition _));
        Assert.False(InvitationCursor.TryDecode(value, out AccountInvitationCursorPosition _));
    }

    [Fact]
    public void Invitation_cursors_require_utc_positions_when_encoding()
    {
        var organization = new OrganizationInvitationCursorPosition(
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(3)),
            new InvitationId(Guid.Parse("00000000-0000-0000-0000-000000000001")));
        var account = new AccountInvitationCursorPosition(
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(3)),
            new InvitationId(Guid.Parse("00000000-0000-0000-0000-000000000001")));

        Assert.Throws<ArgumentException>(() => InvitationCursor.Encode(organization));
        Assert.Throws<ArgumentException>(() => InvitationCursor.Encode(account));
    }
}
