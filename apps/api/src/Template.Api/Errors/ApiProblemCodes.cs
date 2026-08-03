namespace Template.Api.Errors;

internal static class ApiProblemCodes
{
    internal const string InvalidRequest = "invalid_request";
    internal const string ValidationFailed = "validation_failed";
    internal const string Unauthorized = "unauthorized";
    internal const string Forbidden = "forbidden";
    internal const string NotFound = "not_found";
    internal const string MethodNotAllowed = "method_not_allowed";
    internal const string NotAcceptable = "not_acceptable";
    internal const string InternalError = "internal_error";
    internal const string AntiforgeryFailed = "antiforgery_failed";
    internal const string LocalAuthInvalidCredentials = "local_auth_invalid_credentials";
    internal const string LocalAuthUserRequired = "local_auth_user_required";
    internal const string LocalAuthDisabled = "local_auth_disabled";
    internal const string LocalAuthUserExists = "local_auth_user_exists";
    internal const string RateLimited = "rate_limited";
    internal const string InvalidReturnUrl = "invalid_return_url";
    internal const string ExternalProviderNotConfigured =
        "external_provider_not_configured";
    internal const string AlreadyAuthenticated = "already_authenticated";
    internal const string ExternalAuthFailed = "external_auth_failed";
    internal const string ExternalEmailRequired = "external_email_required";
    internal const string ExternalEmailUnverified =
        "external_email_unverified";
    internal const string ExternalIdentityConflict =
        "external_identity_conflict";
    internal const string ExternalEmailConflict = "external_email_conflict";
    internal const string OAuthFlowContextChanged =
        "oauth_flow_context_changed";
    internal const string InvalidCursor = "invalid_cursor";
    internal const string ExternalConnectionRequired =
        "external_connection_required";
    internal const string ExternalConnectionNotFound =
        "external_connection_not_found";
    internal const string AccountSessionNotFound =
        "account_session_not_found";
    internal const string CurrentSessionCannotBeRevoked =
        "current_session_cannot_be_revoked";
    internal const string ConcurrencyConflict = "concurrency_conflict";
    internal const string OrganizationOwnershipTransferRequired =
        "organization_ownership_transfer_required";
    internal const string OrganizationNotFound = "organization_not_found";
    internal const string OrganizationPermissionDenied =
        "organization_permission_denied";
    internal const string OrganizationNameConflict =
        "organization_name_conflict";
    internal const string OrganizationSlugConflict =
        "organization_slug_conflict";
    internal const string LastOrganizationRequired =
        "last_organization_required";
    internal const string OrganizationConfirmationMismatch =
        "organization_confirmation_mismatch";
    internal const string MemberNotFound = "member_not_found";
    internal const string TargetUserNotFound = "target_user_not_found";
    internal const string MemberAlreadyExists = "member_already_exists";
    internal const string MemberRoleUnchanged = "member_role_unchanged";
    internal const string RoleAssignmentForbidden =
        "role_assignment_forbidden";
    internal const string MemberDomainAcknowledgementRequired =
        "member_domain_acknowledgement_required";
    internal const string TeamNotFound = "team_not_found";
    internal const string TeamPermissionDenied = "team_permission_denied";
    internal const string TeamNameConflict = "team_name_conflict";
    internal const string TeamNameUnchanged = "team_name_unchanged";
    internal const string TeamMemberNotFound = "team_member_not_found";
    internal const string TeamMemberAlreadyExists =
        "team_member_already_exists";
    internal const string InvitationNotFound = "invitation_not_found";
    internal const string InvitationPermissionDenied =
        "invitation_permission_denied";
    internal const string InvitationAlreadyExists =
        "invitation_already_exists";
    internal const string InvitationRecipientAlreadyMember =
        "invitation_recipient_already_member";
    internal const string InvitationTeamInvalid = "invitation_team_invalid";
    internal const string InvitationDomainRestricted =
        "invitation_domain_restricted";
    internal const string InvitationRecipientMismatch =
        "invitation_recipient_mismatch";
    internal const string InvitationEmailVerificationRequired =
        "invitation_email_verification_required";
    internal const string InvitationExpired = "invitation_expired";
    internal const string InvitationNotPending = "invitation_not_pending";
    internal const string InvitationMembershipConflict =
        "invitation_membership_conflict";
    internal const string InvitationLimitReached =
        "invitation_limit_reached";
    internal const string ApiKeyNotFound = "api_key_not_found";
    internal const string ApiKeyPermissionDenied = "api_key_permission_denied";
    internal const string ApiKeyUpdateUnchanged = "api_key_update_unchanged";
    internal const string ApiKeyMissing = "api_key_missing";
    internal const string ApiKeyInvalid = "api_key_invalid";
    internal const string ApiKeyRateLimited = "api_key_rate_limited";
    internal const string OrganizationAccessDenied = "organization_access_denied";
}
