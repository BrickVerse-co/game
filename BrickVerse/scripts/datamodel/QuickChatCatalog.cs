using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Datamodel;

public static class QuickChatCatalog
{
	public sealed record Group(string Name, string[] Phrases);
	public static readonly Group[] Groups = [
		new("Greetings", ["Hi!", "Hello!", "Good morning!", "Nice to meet you!", "Welcome!", "Bye!", "See you later!"]),
		new("Social", ["Want to be friends?", "Let's play together!", "Follow me!", "Come with me!", "Wait for me!", "Thank you!", "You're welcome!"]),
		new("Game", ["Good game!", "Nice job!", "Great teamwork!", "Ready?", "Let's go!", "Try again!", "I need help!", "Can you help me?"]),
		new("Answers", ["Yes", "No", "Maybe", "Okay!", "I don't know", "One moment", "Sorry!", "No problem!"]),
		new("Safety", ["Stop, please.", "Please leave me alone.", "I am getting an adult.", "Let's keep this friendly.", "I need to go now."])
	];
	public static IReadOnlyList<string> Phrases { get; } = Groups.SelectMany(group => group.Phrases).ToArray();
}
