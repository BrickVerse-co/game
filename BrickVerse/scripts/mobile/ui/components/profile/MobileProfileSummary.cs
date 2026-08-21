using System;
using BrickVerse.Datamodel;
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Utils;
using Godot;
namespace BrickVerse.Mobile.UI;
public partial class MobileProfileSummary : VBoxContainer
{
	private BrickversianModel? _model; private string _userId = "";
	public override void _Ready() { GetNode<Button>("Showcase/Mode/TwoD").Pressed += () => SetMode(false); GetNode<Button>("Showcase/Mode/ThreeD").Pressed += () => SetMode(true); }
	public void Configure(string userId, string description, int visits, int views, int posts, int friends, int followers, int following, string joined, string lastSeen, string bodyshotUrl)
	{
		_userId = userId; GetNode<Label>("About/Padding/Description").Text = description; GridContainer stats = GetNode<GridContainer>("Stats"); foreach (Node child in stats.GetChildren()) child.QueueFree();
		AddStat(stats, "Member since", Date(joined)); AddStat(stats, "Last seen", Relative(lastSeen)); AddStat(stats, "Friends", friends.ToString("N0")); AddStat(stats, "Followers", followers.ToString("N0")); AddStat(stats, "Following", following.ToString("N0")); AddStat(stats, "World visits", visits.ToString("N0")); AddStat(stats, "Profile views", views.ToString("N0")); AddStat(stats, "Forum posts", posts.ToString("N0")); _ = LoadBodyshot(bodyshotUrl);
	}
	private void SetMode(bool threeD) { GetNode<TextureRect>("Showcase/Bodyshot").Visible = !threeD; GetNode<Control>("Showcase/Preview3D").Visible = threeD; StyleBoxFlat active = Pill("0097FF"), idle = Pill("1A1E24"); GetNode<Button>("Showcase/Mode/TwoD").AddThemeStyleboxOverride("normal", threeD ? idle : active); GetNode<Button>("Showcase/Mode/ThreeD").AddThemeStyleboxOverride("normal", threeD ? active : idle); if (!threeD || _model != null) return; _model = new BrickversianModel(); GetNode<SubViewport>("Showcase/Preview3D/Viewport").AddChild(_model.GDNode); _model.InitEntry(); _model.Position = Vector3.Zero; _model.LoadAppearance(_userId, false); }
	private static StyleBoxFlat Pill(string color) => new() { BgColor = Color.FromHtml(color), CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18, CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18 };
	private async System.Threading.Tasks.Task LoadBodyshot(string supplied) { string url = supplied; if (string.IsNullOrWhiteSpace(url)) url = await BVAPI.ResolveThumbnailUrl("USER_BODYSHOT", _userId); if (string.IsNullOrWhiteSpace(url) || !IsInsideTree()) return; TextureRect image = GetNode<TextureRect>("Showcase/Bodyshot"); WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = url }, resource => { if (IsInstanceValid(image)) image.Texture = (Texture2D)resource; }); }
	private static void AddStat(GridContainer grid, string label, string value) { var panel = new PanelContainer { CustomMinimumSize = new Vector2(104, 70), SizeFlagsHorizontal = SizeFlags.ExpandFill }; var style = new StyleBoxFlat { BgColor = Color.FromHtml("14171C"), BorderColor = Color.FromHtml("272C34"), BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1, ContentMarginTop = 9, ContentMarginBottom = 9, ContentMarginLeft = 6, ContentMarginRight = 6, CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10 }; panel.AddThemeStyleboxOverride("panel", style); var copy = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; copy.AddThemeConstantOverride("separation", 3); var number = new Label { Text = value, HorizontalAlignment = HorizontalAlignment.Center }; number.AddThemeFontSizeOverride("font_size", 16); copy.AddChild(number); var caption = new Label { Text = label, HorizontalAlignment = HorizontalAlignment.Center }; caption.AddThemeFontSizeOverride("font_size", 11); caption.AddThemeColorOverride("font_color", Color.FromHtml("A6AFBB")); copy.AddChild(caption); panel.AddChild(copy); grid.AddChild(panel); }
	private static string Date(string raw) => DateTime.TryParse(raw, out DateTime date) ? date.ToLocalTime().ToString("MMM yyyy") : "—";
	private static string Relative(string raw) { if (!DateTime.TryParse(raw, out DateTime date)) return "Unknown"; TimeSpan age = DateTime.UtcNow - date.ToUniversalTime(); return age.TotalMinutes < 5 ? "Online" : age.TotalHours < 24 ? $"{Math.Max(1, (int)age.TotalHours)}h ago" : $"{Math.Max(1, (int)age.TotalDays)}d ago"; }
	public override void _ExitTree() { _model?.Delete(); _model = null; base._ExitTree(); }
}
