using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using UnityModManagerNet;
using HarmonyLib;

namespace DuskersScavenger {
	public static class Main {
		static List<CommandDefinition> commands;
		static CommandDefinition command;

		static List<DroneUpgradeDefinition> upgradeDefinitions;
		static int upgradeId;
		static DroneUpgradeDefinition upgradeDefinition;

		static Harmony harmony;

		static int FindUpgradeId() {
			int min = (int) DroneUpgradeType.NumberOfUpgrades;
			Random random = new Random();

			int upgradeId;

			do {
				upgradeId = random.Next(int.MaxValue - min) + min + 1;
			} while (upgradeDefinitions.Exists((DroneUpgradeDefinition def) => (int) def.Type == upgradeId));

			return upgradeId;
		}

		public static bool Load(UnityModManager.ModEntry entry) {
			commands = new List<CommandDefinition>();
			command = new CommandDefinition("scavenger", "salvages scrap from failing upgrades", "", "0", "false", "false", "false", "", "", "", "true", "false");
			command.DetailedDescription.Add(new ConsoleMessage("\t\tNo command to execute - applies automatically when installed on a drone", ConsoleMessageType.Info));
			command.DetailedDescription.Add(new ConsoleMessage("\t\t", ConsoleMessageType.Info));
			command.DetailedDescription.Add(new ConsoleMessage("\t\tRecovers scrap as other drone upgrades deteriorate", ConsoleMessageType.Info));
			command.DetailedDescription.Add(new ConsoleMessage("\t\tScavenger will deteriorate an equal amount in return", ConsoleMessageType.Info));
			command.DetailedDescription.Add(new ConsoleMessage("\t\tThe amount of scrap salvaged increases as upgrades get closer to failure", ConsoleMessageType.Info));
			command.DetailedDescription.Add(new ConsoleMessage("\t\t", ConsoleMessageType.Info));
			command.DetailedDescription.Add(new ConsoleMessage("\t\tScavenger mod by <color=#00c2ff>Logan</color><color=#f04040>Dark</color>", ConsoleMessageType.Info));
			commands.Add(command);

			upgradeDefinitions = (List<DroneUpgradeDefinition>) AccessTools.Field(typeof(DroneUpgradeFactory), "_upgradeDefinitions").GetValue(null);
			upgradeId = FindUpgradeId();
			upgradeDefinition = new DroneUpgradeDefinition(upgradeId.ToString(), "true", "Scavenger", "salvages scrap from other deteriorating upgrades", "0", "0", "8", "0", "0", "0", "4");

			harmony = new Harmony(entry.Info.Id);

			entry.OnToggle = OnToggle;

			return true;
		}

		static bool OnToggle(UnityModManager.ModEntry entry, bool active) {
			if (active) {
				harmony.PatchAll(Assembly.GetExecutingAssembly());
				upgradeDefinitions.Add(upgradeDefinition);
			} else {
				upgradeDefinitions.Remove(upgradeDefinition);
				harmony.UnpatchAll(entry.Info.Id);
			}

			return true;
		}

		public class ScavengerUpgrade : BaseDroneUpgrade {
			public ScavengerUpgrade(DroneUpgradeDefinition definition) : base(definition) { }

			public override string CommandValue => "scavenger";

			public override List<CommandDefinition> QueryAvailableCommands() => commands;

			bool scavengedThisMission = false;
			public int lootCount = 0;

			public static void ProcessBreakage(BaseDroneUpgrade broken) {
				foreach (BaseDroneUpgrade upgrade in broken.drone.Upgrades) {
					if (upgrade != null && (int) upgrade.Definition.Type == upgradeId) {
						((ScavengerUpgrade) upgrade).OnSiblingBreakage(broken);
					}
				}
			}

			void OnSiblingBreakage(BaseDroneUpgrade broken) {
				if (BrokenState != BrokenStateEnum.Broken) {
					Break();
					scavengedThisMission = true;
					lootCount += 3;
				}
			}

			public static void ProcessDamage(BaseDroneUpgrade damaged, float damage) {
				foreach (BaseDroneUpgrade upgrade in damaged.drone.Upgrades) {
					if (upgrade != null && (int) upgrade.Definition.Type == upgradeId) {
						((ScavengerUpgrade) upgrade).OnSiblingDamaged(damaged, damage);
					}
				}
			}

			void OnSiblingDamaged(BaseDroneUpgrade damaged, float damage) {
				if (BrokenState != BrokenStateEnum.Broken) {
					BreakProbability += damage;
					scavengedThisMission = true;

					float percentBefore = damaged.BreakProbability - damage;
					lootCount += (int) Math.Floor(percentBefore / 5);
					lootCount += (int) Math.Floor(damaged.BreakProbability / 2) - (int) Math.Floor(percentBefore / 2);
				}
			}

			public void OnMissionEnd() {
				if (scavengedThisMission) {
					NumMissions++;
				}

				scavengedThisMission = false;
			}
		}

		// allow creating Scavenger upgrades
		[HarmonyPatch(typeof(DroneUpgradeFactory))]
		[HarmonyPatch("CreateUpgradeInstance", new[] { typeof(DroneUpgradeType), typeof(int) })]
		static class OnCreateUpgradeInstance {
			static ConstructorInfo gatherCtor = AccessTools.Constructor(typeof(GathererUpgrade), new[] { typeof(DroneUpgradeDefinition) });
			static ConstructorInfo scavengerCtor = AccessTools.Constructor(typeof(ScavengerUpgrade), new[] { typeof(DroneUpgradeDefinition) });

			public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il) {
				CodeInstruction lastInstr = null;

				Label? switchEndLabel = null;
				CodeInstruction loadDefinition = null;
				CodeInstruction storeUpgrade = null;

				foreach (CodeInstruction instr in instructions) {
					if (lastInstr != null) {
						if (lastInstr.opcode == OpCodes.Switch && instr.opcode == OpCodes.Br) switchEndLabel = (Label) instr.operand;
						else if (instr.Is(OpCodes.Newobj, gatherCtor)) loadDefinition = lastInstr;
						else if (lastInstr.Is(OpCodes.Newobj, gatherCtor)) storeUpgrade = instr;
					}

					List<Label> labels = instr.labels;
					if (switchEndLabel is Label switchEndLabel2 && labels.Contains(switchEndLabel2)) {
						Label afterLabel = il.DefineLabel();
						labels.Remove(switchEndLabel2);
						labels.Add(afterLabel);

						yield return new CodeInstruction(OpCodes.Ldarg_0).WithLabels(new[] { switchEndLabel2 });
						yield return new CodeInstruction(OpCodes.Ldc_I4, upgradeId);
						yield return new CodeInstruction(OpCodes.Bne_Un, afterLabel);
						yield return new CodeInstruction(loadDefinition.opcode, loadDefinition.operand);
						yield return new CodeInstruction(OpCodes.Newobj, scavengerCtor);
						yield return new CodeInstruction(storeUpgrade.opcode, storeUpgrade.operand);
					}

					yield return lastInstr = instr;
				}
			}
		}

		// show Scavenger in help manual
		[HarmonyPatch(typeof(HelpManualMenuHelper))]
		[HarmonyPatch("RefreshDroneUpdadeMenu")]
		static class OnDroneUpgradeHelpRefresh {
			public static void Postfix(HelpManualMenuHelper __instance, bool ___useSimpleHelp, HelpManualMenu ___droneUpgrades) {
				if (___useSimpleHelp || GlobalSettings.DiscoveredUpgrades.Contains((DroneUpgradeType) upgradeId)) {
					__instance.AddCommands(___droneUpgrades.MenuItems, commands);
				}
			}
		}

		// make Scavenger save
		[HarmonyPatch(typeof(Enum))]
		[HarmonyPatch("ToString", new Type[] { })]
		static class OnDroneUpgradeTypeToString {
			public static bool Prefix(Enum __instance, ref string __result) {
				if (__instance.GetType() == typeof(DroneUpgradeType) && (int) (Object) __instance == upgradeId) {
					__result = upgradeDefinition.Name;
					return false;
				}

				return true;
			}
		}

		// make Scavenger load
		[HarmonyPatch(typeof(Enum))]
		[HarmonyPatch("Parse", new Type[] { typeof(Type), typeof(string), typeof(bool) })]
		static class OnDroneUpgradeTypeParse {
			public static bool Prefix(Type enumType, string value, ref object __result) {
				if (enumType == typeof(DroneUpgradeType) && value == upgradeDefinition.Name) {
					__result = (DroneUpgradeType) upgradeId;
					return false;
				}

				return true;
			}
		}

		// notify Scavenger upgrades when another upgrade breaks / is damaged naturally
		[HarmonyPatch(typeof(DungeonManager))]
		[HarmonyPatch("BeginExit")]
		static class OnDungeonBeginExit {
			static MethodInfo breakMethod = AccessTools.Method(typeof(BaseDroneUpgrade), "Break");

			static MethodInfo processBreakage = AccessTools.Method(typeof(ScavengerUpgrade), "ProcessBreakage", new[] { typeof(BaseDroneUpgrade) });
			static MethodInfo processDamage = AccessTools.Method(typeof(ScavengerUpgrade), "ProcessDamage", new[] { typeof(BaseDroneUpgrade), typeof(float) });

			static MethodInfo getUpgradeBreakFactor = AccessTools.PropertyGetter(typeof(BaseDroneUpgrade), "UpgradeBreakFactor");
			static MethodInfo getBreakProbability = AccessTools.PropertyGetter(typeof(BaseDroneUpgrade), "BreakProbability");
			static MethodInfo setBreakProbability = AccessTools.PropertySetter(typeof(BaseDroneUpgrade), "BreakProbability");

			public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il) {
				CodeInstruction lastLoad = null;
				CodeInstruction lastInstr = null;

				bool hookingDamage = false;
				CodeInstruction loadUpgrade = null;
				CodeInstruction loadDamage = null;

				foreach (CodeInstruction instr in instructions) {
					// always inject after the target so that multiple of these patches can coexist
					if (instr.Is(OpCodes.Callvirt, breakMethod)) {
						yield return instr;
						yield return lastLoad = new CodeInstruction(lastLoad.opcode, lastLoad.operand);
						yield return lastInstr = new CodeInstruction(OpCodes.Call, processBreakage);
						continue;
					} else if (instr.Is(OpCodes.Callvirt, getUpgradeBreakFactor)) {
						hookingDamage = true;
					} else if (hookingDamage && instr.Is(OpCodes.Callvirt, getBreakProbability)) {
						loadUpgrade = new CodeInstruction(lastLoad.opcode, lastLoad.operand);
					} else if (hookingDamage && lastInstr != null && lastInstr.Is(OpCodes.Callvirt, getBreakProbability)) {
						loadDamage = new CodeInstruction(instr.opcode, instr.operand);
					} else if (hookingDamage && lastInstr != null && lastInstr.Is(OpCodes.Callvirt, setBreakProbability)) {
						yield return new CodeInstruction(loadUpgrade);
						yield return new CodeInstruction(loadDamage);
						yield return new CodeInstruction(OpCodes.Call, processDamage);
						hookingDamage = false;
					}

					if (instr.opcode.Name.StartsWith("ldloc")) {
						lastLoad = instr;
					}

					yield return lastInstr = instr;
				}
			}

			public static void Postfix(DroneManager ___droneManager) {
				foreach (Drone drone in ___droneManager.dronesList) {
					foreach (BaseDroneUpgrade upgrade in drone.Upgrades) {
						if (upgrade != null && (int) upgrade.Definition.Type == upgradeId) {
							((ScavengerUpgrade) upgrade).OnMissionEnd();
						}
					}
				}
			}
		}

		// allows the granting of scrap to the player by using the loot system as the Gather upgrade does
		[HarmonyPatch(typeof(Drone))]
		[HarmonyPatch("GetLootCount", new[] { typeof(bool) })]
		static class OnGetLootCount {
			public static void Postfix(Drone __instance, bool clearLoot, ref int __result) {
				foreach (BaseDroneUpgrade upgrade in __instance.Upgrades) {
					if (upgrade != null && (int) upgrade.Definition.Type == upgradeId) {
						__result += ((ScavengerUpgrade) upgrade).lootCount;
						if (clearLoot) ((ScavengerUpgrade) upgrade).lootCount = 0;
					}
				}
			}
		}

		// allows the granting of scrap to the player by using the loot system as the Gather upgrade does
		[HarmonyPatch(typeof(DroneManager))]
		[HarmonyPatch("RandomlyChooseUpgrades", new[] { typeof(List<Drone>), typeof(int), typeof(int), typeof(bool), typeof(bool), typeof(Random) })]
		static class OnRandomlyChooseUpgrades {
			static FieldInfo gameMode = AccessTools.Field(typeof(GlobalSettings), "gameMode");

			static MethodInfo randomNext = AccessTools.Method(typeof(Random), "Next", new[] { typeof(int), typeof(int) });

			static DroneUpgradeType randomlyChooseUpgrade(Random rng) {
				DroneUpgradeDefinition definition;

				bool limitBruteTurret = GlobalSettings.gameMode == GameModeEnum.Normal && GameSaveFile.Get<int>("RESETS", 0) < 1;

				do {
					definition = upgradeDefinitions[rng.Next(0, upgradeDefinitions.Count)];
				} while (limitBruteTurret && definition.Type == DroneUpgradeType.BruteTurret);

				return definition.Type;
			}

			static MethodInfo properRNG = AccessTools.Method(typeof(OnRandomlyChooseUpgrades), "randomlyChooseUpgrade");

			public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il) {
				CodeInstruction lastInstr = null;

				bool patchingNextRNG = false;
				LocalBuilder id = null;

				foreach (CodeInstruction instr in instructions) {
					if (instr.Is(OpCodes.Ldsfld, gameMode)) {
						patchingNextRNG = true;
					} else if (patchingNextRNG && lastInstr.Is(OpCodes.Callvirt, randomNext)) {
						id = (LocalBuilder) instr.operand;
					}

					yield return instr;

					if (lastInstr != null && id != null && lastInstr.Is(OpCodes.Ldloc_S, id) && instr.opcode == OpCodes.Stloc_S) {
						patchingNextRNG = false;
						yield return new CodeInstruction(OpCodes.Ldarg_S, 6);
						yield return new CodeInstruction(OpCodes.Call, properRNG);
						yield return new CodeInstruction(instr.opcode, instr.operand);
					}

					lastInstr = instr;
				}
			}
		}

		// for testing the RNG: places tons of drones in each room
		// use drone operator training to test if scavenger shows up randomly
#if DEBUG
		[HarmonyPatch(typeof(DungeonManager))]
		[HarmonyPatch("Start")]
		static class OnDungeonStart {
			static MethodInfo initializeRoomPower = AccessTools.Method(typeof(DungeonManager), "InitializeRoomPower");

			static void MakeLotsOfFuckingDrones(DroneManager droneManager) {
				UnityModManager.Logger.Log("making lots of fucking drones");

				int lastLootableDroneId = 10 + droneManager.LootableDronesList.Count;

				foreach (Room room in DungeonManager.Instance.rooms) {
					for (int i = 0; i < 5; i++) {
						droneManager.PlaceLootableDroneInRoom(room, ref lastLootableDroneId, true);
					}
				}
			}

			static FieldInfo droneManager = AccessTools.Field(typeof(DungeonManager), "droneManager");
			static MethodInfo makeLotsOfFuckingDrones = AccessTools.Method(typeof(OnDungeonStart), "MakeLotsOfFuckingDrones", new[] { typeof(DroneManager) });

			public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il) {
				foreach (CodeInstruction instr in instructions) {
					yield return instr;

					if (instr.Is(OpCodes.Call, initializeRoomPower)) {
						yield return new CodeInstruction(OpCodes.Ldarg_0);
						yield return new CodeInstruction(OpCodes.Ldfld, droneManager);
						yield return new CodeInstruction(OpCodes.Call, makeLotsOfFuckingDrones);
					}
				}
			}
		}
#endif
	}
}
