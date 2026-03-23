namespace LiveCanvas.Bridge.Protocol;

public static class BridgeMethodNames
{
    public const string GhSessionInfo = "gh_session_info";
    public const string GhNewDocument = "gh_new_document";
    public const string GhListAllowedComponents = "gh_list_allowed_components";
    public const string GhAddComponent = "gh_add_component";
    public const string GhConfigureComponent = "gh_configure_component";
    public const string GhConfigureComponentV2 = "gh_configure_component_v2";
    public const string GhConnect = "gh_connect";
    public const string GhDeleteComponent = "gh_delete_component";
    public const string GhSolve = "gh_solve";
    public const string GhInspectDocument = "gh_inspect_document";
    public const string GhCapturePreview = "gh_capture_preview";
    public const string GhSaveDocument = "gh_save_document";

    public static readonly IReadOnlyList<string> All =
    [
        GhSessionInfo,
        GhNewDocument,
        GhListAllowedComponents,
        GhAddComponent,
        GhConfigureComponent,
        GhConfigureComponentV2,
        GhConnect,
        GhDeleteComponent,
        GhSolve,
        GhInspectDocument,
        GhCapturePreview,
        GhSaveDocument
    ];
}
