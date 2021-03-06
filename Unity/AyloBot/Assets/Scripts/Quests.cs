using UnityEngine;
using System.Collections.Generic;

public class Quests
{
	public Quests()
	{
		//var robot = GameObject.Find("Robot").GetComponent<Robot>();
		// 20160603 rtharp
		// was in robot but moved to Constants
		// as Quests is available when Robot isn't
		openColor = Constants.openQuestColor;
		closedColor = Constants.closedQuestColor;
	}

	public void Add(string quest)
	{
		var questObj = new Quest (quest);
		questObj.richText=false;
		quests.Add(questObj);
		UpdateQuests();
	}

	public void AddRichText(string quest)
	{
		var questObj = new Quest (quest);
		questObj.richText=true;
		quests.Add(questObj);
		UpdateQuests();
	}

	public void Update(int iQuest, string quest)
	{
		if(IsBadIndex(iQuest))
			return;
		
		quests[iQuest].text = quest;
		UpdateQuests();
	}
	
	public void Close(int iQuest)
	{
		if(IsBadIndex(iQuest))
			return;
		
		quests[iQuest].closed = true;
		UpdateQuests();
	}
	
	public void Open(int iQuest)
	{
		if(IsBadIndex(iQuest))
			return;
		
		quests[iQuest].closed = false;
		UpdateQuests();
	}
	
	public void Remove(int iQuest)
	{
		if(IsBadIndex(iQuest))
			return;
		
		quests.RemoveAt(iQuest);
		UpdateQuests();
	}
	
	// Change this function to change the appearance of the quests.
	void UpdateQuests()
	{
		string questsString = "";
		foreach(var quest in quests)
		{
			var color = openColor;

			if(quest.closed)
				color = closedColor;

			if (quest.richText) {

					questsString +=  "\n" + quest.text;

			} else {

					questsString += "<color=#" + color + ">";
					questsString +=  "\n" + quest.text;
					questsString += "</color>";

			}
		}
		
		GameObject.Find("Quests").GetComponent<TextMesh>().text = questsString;
	}
	
	bool IsBadIndex(int iQuest)
	{
		return iQuest < 0 || iQuest > quests.Count;
	}
	
	class Quest
	{
		public string text;
		public bool richText;
		public bool closed;
		
		public Quest(string text)
		{
			this.text = text;
			this.richText=false;
			closed = false;
		}
	}

	string openColor;
	string closedColor;
	IList<Quest> quests = new List<Quest>();
}
