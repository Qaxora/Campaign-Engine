using System.Security.Claims;
using CampaignEngine.Api.Http;
using CampaignEngine.Api.Security;
using CampaignEngine.Infrastructure.Platform;
using CampaignEngine.Infrastructure.Services;

namespace CampaignEngine.Api.Endpoints;

/// <summary>Sign-in for people (the web app), organization settings and members.</summary>
public static class AccountEndpoints
{
    public sealed record RegisterRequest(string Email, string Password, string Name, string? OrganizationName);

    public sealed record LoginRequest(string Email, string Password, string? Organization);

    public sealed record SwitchOrganizationRequest(Guid OrganizationId);

    public sealed record CreateOrganizationRequest(string Name);

    public sealed record RenameOrganizationRequest(string Name);

    public sealed record AddMemberRequest(string Email, string Role);

    public sealed record ChangeRoleRequest(string Role);

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth").WithTags("Authentication");

        auth.MapPost("/register", async (RegisterRequest request, IdentityService identities, CancellationToken ct) =>
                Results.Ok(await identities.RegisterAsync(request.Email, request.Password, request.Name, request.OrganizationName, ct)))
            .AllowAnonymous()
            .WithSummary("Create an account (and, with organizationName, an organization owned by it)")
            .WithDescription("Returns a session token for `Authorization: Bearer`. The web app keeps it in an httpOnly cookie.")
            .Produces<SignInResult>();

        auth.MapPost("/login", async (LoginRequest request, IdentityService identities, CancellationToken ct) =>
                Results.Ok(await identities.LoginAsync(request.Email, request.Password, request.Organization, ct)))
            .AllowAnonymous()
            .WithSummary("Sign in")
            .Produces<SignInResult>();

        auth.MapPost("/logout", async (ClaimsPrincipal user, IdentityService identities, CancellationToken ct) =>
            {
                await identities.LogoutAsync(user.SessionId(), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Policies.User)
            .WithSummary("End the current session");

        auth.MapGet("/me", async (ClaimsPrincipal user, IdentityService identities, CancellationToken ct) =>
                await identities.DescribeAsync(user.SessionId(), ct))
            .RequireAuthorization(Policies.User)
            .WithSummary("The signed-in user, the current organization and all memberships");

        auth.MapPost("/switch-organization", async (SwitchOrganizationRequest request, ClaimsPrincipal user, IdentityService identities, CancellationToken ct) =>
                await identities.SwitchOrganizationAsync(user.SessionId(), request.OrganizationId, ct))
            .RequireAuthorization(Policies.User)
            .WithSummary("Act for another organization you belong to");

        auth.MapPost("/organizations", async (CreateOrganizationRequest request, ClaimsPrincipal user, IdentityService identities, CancellationToken ct) =>
                await identities.CreateOrganizationAsync(user.SessionId(), request.Name, ct))
            .RequireAuthorization(Policies.User)
            .WithSummary("Create another organization (you become its owner) and switch to it");

        var organization = app.MapGroup("/api/v1/organization").WithTags("Organization");

        organization.MapGet("/", async Task<IResult> (ITenantContext tenant, OrganizationService organizations, CancellationToken ct) =>
                await organizations.GetAsync(tenant.RequiredTenantId, ct) is { } org ? Results.Ok(org) : Results.NotFound())
            .RequireAuthorization(Policies.Read)
            .WithSummary("The current organization")
            .Produces<Organization>();

        organization.MapPut("/", async (RenameOrganizationRequest request, ITenantContext tenant, OrganizationService organizations, CancellationToken ct) =>
                await organizations.RenameAsync(tenant.RequiredTenantId, request.Name, ct))
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Rename the organization");

        var members = app.MapGroup("/api/v1/members").WithTags("Members");

        members.MapGet("/", async (MemberService service, CancellationToken ct) => await service.ListAsync(ct))
            .RequireAuthorization(Policies.Read)
            .WithSummary("Members of the organization");

        members.MapPost("/", async (AddMemberRequest request, ClaimsPrincipal user, MemberService service, CancellationToken ct) =>
            {
                var member = await service.AddAsync(request.Email, ParseRole(request.Role), user.MemberRole(), ct);
                return Results.Created($"/api/v1/members/{member.UserId}", member);
            })
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Add an existing account by email with a role (owner, admin, member)")
            .Produces<Member>(StatusCodes.Status201Created);

        members.MapPut("/{userId:guid}", async (Guid userId, ChangeRoleRequest request, ClaimsPrincipal user, MemberService service, CancellationToken ct) =>
                await service.ChangeRoleAsync(userId, ParseRole(request.Role), user.MemberRole(), ct))
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Change a member's role; only owners manage ownership, and the last owner stays");

        members.MapDelete("/{userId:guid}", async (Guid userId, ClaimsPrincipal user, MemberService service, CancellationToken ct) =>
            {
                await service.RemoveAsync(userId, user.MemberRole(), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Remove a member");

        return app;
    }

    private static MemberRole ParseRole(string role) =>
        QueryEnum.Parse<MemberRole>(role, "role") ?? throw new ValidationException(["role is required."]);
}
