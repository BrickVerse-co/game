// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Net.Http;
using System.Text.Json;
using BrickVerse.Shared;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileReportDialog : Window
{
	private OptionButton _reason = null!;
	private TextEdit _description = null!;
	private Label _status = null!;
	private Button _submit = null!;
	private string _targetType = "";
	private string _targetId = "";

	public override void _Ready()
	{
		_reason = GetNode<OptionButton>("Layout/Reason");
		_description = GetNode<TextEdit>("Layout/Description");
		_status = GetNode<Label>("Layout/Status");
		_submit = GetNode<Button>("Layout/Actions/Submit");
		GetNode<Button>("Layout/Actions/Cancel").Pressed += QueueFree;
		_submit.Pressed += Submit;
		CloseRequested += QueueFree;
		_ = LoadReasons();
	}

	public static MobileReportDialog Open(Node owner, string targetType, string targetId)
	{
		MobileReportDialog dialog = GD.Load<PackedScene>("res://scenes/mobile/views/report.tscn").Instantiate<MobileReportDialog>();
		owner.GetTree().Root.AddChild(dialog);
		dialog._targetType = targetType;
		dialog._targetId = targetId;
		dialog.PopupCentered();
		return dialog;
	}

	private async System.Threading.Tasks.Task LoadReasons()
	{
		_reason.Clear();
		try
		{
			using JsonDocument document = await BVAPI.GetJson("/v3/social/report/config");
			if (document.RootElement.TryGetProperty("reasons", out JsonElement reasons) && reasons.ValueKind == JsonValueKind.Array)
				foreach (JsonElement reason in reasons.EnumerateArray())
				{
					string label = reason.TryGetProperty("label", out JsonElement labelNode) ? labelNode.GetString() ?? "Other" : "Other";
					string value = reason.TryGetProperty("value", out JsonElement valueNode) ? valueNode.GetString() ?? "other" : "other";
					_reason.AddItem(label);
					_reason.SetItemMetadata(_reason.ItemCount - 1, value);
				}
		}
		catch (Exception exception) { BV.PrintErr(exception); }
		if (_reason.ItemCount == 0)
		{
			_reason.AddItem("Other policy violation");
			_reason.SetItemMetadata(0, "other");
		}
	}

	private async void Submit()
	{
		_submit.Disabled = true;
		_status.Text = "Submitting report…";
		try
		{
			string reason = _reason.GetItemMetadata(_reason.Selected).AsString();
			string json = JsonSerializer.Serialize(new { targetType = _targetType, targetId = _targetId, reason, description = _description.Text.Trim() });
			using JsonDocument _ = await BVAPI.SendJson(HttpMethod.Post, "/v3/social/reports", json);
			_status.Text = "Report submitted. Thank you for helping keep BrickVerse safe.";
			_submit.Text = "Submitted";
		}
		catch (Exception exception)
		{
			_status.Text = exception.Message;
			_submit.Disabled = false;
		}
	}
}
