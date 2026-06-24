// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Polytoria.Datamodel.Services;

[Static("Filter"), ExplorerExclude]
[SaveIgnore]
public sealed partial class FilterService : Instance
{
	private static List<string> _profanityList = [];

	public override void Init()
	{
		base.Init();
		LoadFilter();
	}

	private static async void LoadFilter()
	{
		try
		{
			if (OS.HasFeature("offline"))
			{
				_profanityList = ["swear"];
				return;
			}
			string rawdata = await PolyAPI.GetProfanityList();
			_profanityList = [.. rawdata.Split(["\n"], StringSplitOptions.RemoveEmptyEntries)];
		}
		catch (Exception err)
		{
			PT.PrintErr("Failed to get profanity list: ", err);
		}
	}

	[ScriptMethod]
	public static string Filter(string input)
	{
		if (_profanityList.Count == 0)
		{
			LoadFilter();
			return new string('*', input.Length);
		}
		string[] words = input.Split([" "], StringSplitOptions.RemoveEmptyEntries);
		List<string> filteredWords = [];
		foreach (string word in words)
		{
			bool found = false;
			foreach (string filter in _profanityList)
			{
				string f = filter.Trim();
				if (f.Contains('*'))
				{
					string regex = f.Replace("*", ".*");
					if (Regex.IsMatch(word, regex, RegexOptions.IgnoreCase))
					{
						filteredWords.Add(new string('*', word.Length));
						found = true;
						break;
					}
				}
				else
				{
					if (word.Equals(f, StringComparison.OrdinalIgnoreCase))
					{
						filteredWords.Add(new string('*', word.Length));
						found = true;
						break;
					}
				}
			}

			if (!found)
			{
				filteredWords.Add(word);
			}
		}

		return string.Join(" ", filteredWords.ToArray());
	}
}
