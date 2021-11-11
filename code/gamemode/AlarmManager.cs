using System;
using Sandbox;

enum AlarmLevel
{
	None,
	Caution,
	Aware,
	Alert,
	Lockdown,
}

class AlarmManager
{
	private static AlarmLevel CurrentAlarmLevel;
	private static TimeSince LastAlarmLevelChange;

	public static AlarmLevel GetAlarmLevel()
	{
		return CurrentAlarmLevel;
	}

	public static void RaiseAlarmLevel()
	{
		LastAlarmLevelChange = 0;
	}

}
