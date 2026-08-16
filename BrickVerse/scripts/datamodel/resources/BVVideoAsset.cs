using BrickVerse.Attributes;
using BrickVerse.Shared.AssetLoaders;
namespace BrickVerse.Datamodel.Resources;

[Instantiable]
public sealed partial class BVVideoAsset : VideoAsset
{
	private string _videoId = "0";
	[Editable, ScriptProperty] public string VideoID { get => _videoId; set { _videoId = value; QueueLoadResource(); OnPropertyChanged(); } }
	public static void RegisterAsset() => RegisterType<BVVideoAsset>();
	public override void LoadResource() { if (_videoId == "0") return; AssetLoader.Singleton.GetResource(new() { Type = ResourceType.Video, ID = _videoId }, InvokeResourceLoaded); }
}
