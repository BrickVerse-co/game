// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileTransactionCard : PanelContainer
{
	public override void _Ready() => MobileMotion.BindCard(this);

	public void Configure(int amount, bool received, string status, string from, string to, string date)
	{
		Label amountLabel = GetNode<Label>("Layout/Header/Amount");
		amountLabel.Text = $"{(received ? "+" : "−")}{amount:N0} Cubes";
		amountLabel.Modulate = received ? new Color(0.48f, 0.95f, 0.65f) : new Color(1f, 0.72f, 0.55f);
		GetNode<Label>("Layout/Header/Status").Text = status.Replace('_', ' ');
		GetNode<Label>("Layout/Route/From").Text = "From\n" + from;
		GetNode<Label>("Layout/Route/To").Text = "To\n" + to;
		GetNode<Label>("Layout/Date").Text = date;
	}
}
