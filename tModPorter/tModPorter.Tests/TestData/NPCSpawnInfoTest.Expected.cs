using Terraria;
using Terraria.ModLoader;

public class NPCSpawnInfoTest
{
	void Method() {
		NPC.Spawner info = default;
		var a = info.spawnUndergroundDesert;
		var b = info.nearGranite;
		var c = info.invaders;
		var d = info.ZoneLihzhardTemple;
		var e = info.nearMarble;
		;/* tModPorter Note: Removed. Use (NPC.downedPlantBoss && Main.hardMode) instead. */
		var g = info.Player;
		;/* tModPorter Note: Removed. Player floor coordinates are no longer exposed on NPC.Spawner. Use SpawnTileX/SpawnTileY or player position data instead. */
		;/* tModPorter Note: Removed. Player floor coordinates are no longer exposed on NPC.Spawner. Use SpawnTileX/SpawnTileY or player position data instead. */
		var j = info.spawnFriendly;
		var k = info.noWorms;
		var l = info.SafeRangeX;
		var m = info.skyMob;
		var n = info.SpawnTileType;
		var o = info.SpawnTileX;
		var p = info.SpawnTileY;
		var q = info.spawnSpider;
		var r = info.waterTile;
	}
}
