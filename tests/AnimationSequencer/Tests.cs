using System;
using BrickVerse.Formats;
using Godot;

public partial class Tests : Node
{
    public override void _Ready()
    {
        try
        {
            BVAnimationClip clip = new() { Length = 2, Tracks = [
                new() { Path = ".:position", Channel = "property", ValueType = "Vector3", Keys = [new() { Value = [0, 0, 0] }, new() { Time = 2, Value = [10, 4, 2] }] },
                new() { Path = ".:visible", Channel = "property", ValueType = "Bool", Keys = [new() { Value = [1] }, new() { Time = 2, Value = [0] }] },
                new() { Path = "Light:light_color", Channel = "property", ValueType = "Color", Keys = [new() { Value = [1, 0, 0, 1] }, new() { Time = 2, Value = [0, 0, 1, 1] }] }
            ] };
            var copy = BVAnimationFormat.Read(BVAnimationFormat.Write(clip));
            Check(copy.Tracks[2].ValueType == "Color", "Property type survives serialization");
            Animation animation = BVAnimationFormat.ToAnimation(copy);
            Check(animation.ValueTrackGetUpdateMode(1) == Animation.UpdateMode.Discrete, "Visibility uses discrete keys");
            var imported = BVAnimationFormat.FromAnimation("Imported", animation);
            Check(imported.Tracks.Count == 3 && imported.Tracks[2].Keys[1].Value[2] == 1, "Native property tracks round-trip");
            Node3D target = new(); AddChild(target);
            OmniLight3D light = new() { Name = "Light" }; target.AddChild(light);
            AnimationPlayer player = new(); target.AddChild(player);
            AnimationLibrary library = new(); library.AddAnimation("clip", animation); player.AddAnimationLibrary("", library);
            player.Play("clip"); player.Seek(1, true);
            Check(target.Position.IsEqualApprox(new Vector3(5, 2, 1)), "Object position interpolates at midpoint");
            Check(light.LightColor.IsEqualApprox(new Color(0.5f, 0, 0.5f, 1)), "Child color interpolates at midpoint");
            Check(target.Visible, "Visibility holds before next key");
            player.Seek(2, true); Check(!target.Visible, "Visibility changes at key");
            copy.Tracks[0].Keys[0].Value = [1];
            bool rejected = false;
            try { BVAnimationFormat.Validate(copy); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Malformed property values rejected");
            BVAnimationClip legacy = new() { Version = 1, Tracks = [new() { Path = ".", Channel = "rotation", Keys = [new() { Value = [0, 0, 0, 1] }] }] };
            Check(BVAnimationFormat.ToAnimation(BVAnimationFormat.Read(BVAnimationFormat.Write(legacy))).GetTrackCount() == 1, "Version 1 skeletal clips remain supported");
            BVAnimationFormat.Validate(new BVAnimationClip());
            GD.Print("PASS: sequencer serialization, compatibility, validation and native playback");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr(error); GetTree().Quit(1); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print("PASS: ", message);
    }
}
