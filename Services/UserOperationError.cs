namespace MarketApp.Services;

public enum UserOperationError
{
    None,
    NotFound,
    DuplicateUsername,
    InvalidRole,
    LastAdmin,
    SelfDelete
}
