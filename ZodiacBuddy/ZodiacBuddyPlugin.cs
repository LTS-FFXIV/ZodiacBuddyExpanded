using ZodiacBuddy.BonusLight;
using ZodiacBuddy.Stages.Atma;
using ZodiacBuddy.Stages.Brave;
using ZodiacBuddy.Stages.Novus;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
﻿using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using Dalamud.Plugin;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ECommons.GameFunctions;
using ECommons.Interop;
using ECommons.SplatoonAPI;
using ImGuiNET;
using static Dalamud.Interface.Utility.Raii.ImRaii;

namespace ZodiacBuddy
{
    public sealed class ZodiacBuddyPlugin : IDalamudPlugin
    {
        private const string Command = "/zzodiac";

        private readonly AtmaManager animusBuddy;
        private readonly NovusManager novusManager;
        private readonly BraveManager braveManager;

        private readonly WindowSystem windowSystem;
        private readonly ConfigWindow configWindow;

        private readonly DalamudPluginInterface pluginInterface;
        private readonly IChatGui chat;
        private readonly IClientState clientState;
        private readonly IObjectTable objectTable;
        private readonly IDataManager dataManager;
        private readonly IGameGui gameGui;

        private readonly IDictionary<uint, Book> books = new Dictionary<uint, Book>();
        private ISet<uint> atmaWeapons = new HashSet<uint> {
            7824, // Curtana Atma
            7825, // Sphairai Atma
            7826, // Bravura Atma
            7827, // Gae Bolg Atma
            7828, // Artemis Bow Atma
            7829, // Thyrus Atma
            7830, // Stardust Rod Atma
            7831, // Veil of Wiyu Atma
            7832, // Omnilex Atma
            7833, // Holy Shield Atma
            9251, // Yoshimitsu Atma
        };

        public string Name => "ZodiacBuddy";

        public ZodiacBuddyPlugin(DalamudPluginInterface pluginInterface, ICommandManager commands, IChatGui chat, IClientState clientState, IObjectTable objectTable, IDataManager dataManager, IGameGui gameGui)
        {
            this.pluginInterface = pluginInterface;
            this.chat = chat;
            this.clientState = clientState;
            this.objectTable = objectTable;
            this.dataManager = dataManager;
            this.gameGui = gameGui;

            Service.Initialize(pluginInterface);

            this.windowSystem = new WindowSystem("ZodiacBuddy");
            this.configWindow = new ConfigWindow();
            this.windowSystem.AddWindow(this.configWindow);

            Service.Interface.UiBuilder.OpenConfigUi += OnOpenConfigUi;
            Service.Interface.UiBuilder.Draw += windowSystem.Draw;

            Service.CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open a window to edit various settings.",
                ShowInHelp = true,
            });

            this.animusBuddy = new AtmaManager();
            this.novusManager = new NovusManager();
            this.braveManager = new BraveManager();

            PopulateBooks();
        }

        public void Dispose()
        {
            Service.CommandManager.RemoveHandler(Command);
        
            Service.Interface.UiBuilder.Draw -= this.windowSystem.Draw;
            Service.Interface.UiBuilder.OpenConfigUi -= this.OnOpenConfigUi;
        
            this.animusBuddy?.Dispose();
            this.novusManager?.Dispose();
            this.braveManager?.Dispose();
            Service.BonusLightManager?.Dispose();
        }

        private void OnOpenConfigUi() => configWindow.IsOpen = true;

        private void OnCommand(string command, string arguments) => configWindow.IsOpen = true;

        public void PrintMessage(SeString message)
        {
            var sb = new SeStringBuilder()
                .AddUiForeground(45)
                .AddText("[ZodiacBuddy] ")
                .AddUiForegroundOff()
                .Append(message);

            chat.Print(sb.BuiltString);
        }

        public void PrintError(string message) => chat.PrintError($"[ZodiacBuddy] {message}");

        private bool HasAtmaWeaponEquipped() => atmaWeapons.Contains(GetEquippedItem(0)) || atmaWeapons.Contains(GetEquippedItem(1));


        /// <summary>
        /// Print an error message.
        /// </summary>
        /// <param name="message">Message to send.</param>
        public void PrintError(string message)
        {
            Service.ChatGui.PrintError($"[ZodiacBuddy] {message}");
        }
        private bool HasAtmaWeaponEquipped()
        {
            return atmaWeapons.Contains(GetEquippedItem(0)) || atmaWeapons.Contains(GetEquippedItem(1));
        }

        private unsafe uint GetCurrentBook()
        {
            var im = InventoryManager.Instance();
            if (im == null)
                return 0;

            var keyItems = im->GetInventoryContainer(InventoryType.KeyItems);
            for (int i = 0; i < keyItems->Size; ++i)
            {
                var slot = keyItems->GetInventorySlot(i);
                if (slot == null)
                    continue;

                uint itemId = slot->ItemID;
                if (books.ContainsKey(itemId))
                    return itemId;
            }

            return 0;
        }

        private unsafe uint GetEquippedItem(int index)
        {
            var im = InventoryManager.Instance();
            if (im == null) 
                return 0;

            var equipped = im->GetInventoryContainer(InventoryType.EquippedItems);
            if (equipped == null)
                return 0;

            var slot = equipped->GetInventorySlot(index);
            if (slot == null)
                return 0;

            return slot->ItemID;
        }

       private void PopulateBooks()
        {
            var relicNoteSheet = dataManager.GetExcelSheet<Excel.RelicNote>();
            foreach (var row in relicNoteSheet)
            {
                if (row.EventItem?.Row == 0) continue;
                
                var book = new Book();
                foreach (var target in row.MonsterNoteTargetCommon)
                {
                    book.Enemies.Add(target.MonsterNoteTargetCommon.Value.BNpcName.Row);
                }
                books.Add(row.EventItem.Row, book);
            }
        }


        internal sealed class Book
        {
            public IList<uint> Enemies = new List<uint>();

            public override string ToString() => string.Join(",", Enemies);
        }

        private void OnOpenConfigUi()
            => this.configWindow.IsOpen = true;

        private void OnCommand(string command, string arguments)
        {
            this.configWindow.IsOpen = true;
        }
        public void DrawLinesToEnemies()
        {
            // Checks if the player is logged in, not in PvP, the object table is not null, and an Atma weapon is equipped.
            if (!this.clientState.IsLoggedIn || this.clientState.IsPvP || this.objectTable == null || !this.HasAtmaWeaponEquipped())
            {
                return;
            }
        
            uint currentBook = this.GetCurrentBook();
            if (currentBook == 0U)
            {
                return;
            }
        
            RelicNote* relicNotePtr = RelicNote.Instance();
            if ((IntPtr)relicNotePtr == IntPtr.Zero)
            {
                return;
            }
        
            // Retrieve the book information for the current book ID.
            ZodiacFinder.Plugin.Book book = this.books[currentBook];
            // Assume GetPlayerPosition is a method that fetches the player's current position.
            Vector3 playerPosition = GetPlayerPosition(); 
        
            for (int index = 0; index < this.objectTable.Length; ++index)
            {
                GameObject gameObject = this.objectTable[index];
                if (gameObject is BattleNpc battleNpc && ((Character)battleNpc).CurrentHp != 0U)
                {
                    int enemyIndex = book.Enemies.IndexOf(((Character)battleNpc).NameId);
                    // Check if the enemy is listed in the book and the progress on that enemy is less than 3.
                    if (enemyIndex != -1 && ((RelicNote)(IntPtr)relicNotePtr).GetMonsterProgress(enemyIndex) < (byte)3)
                    {
                        // Create a line element from the player to the NPC using the Splatoon API.
                        Element lineToEnemy = new Element(ElementType.LineBetweenTwoFixedCoordinates);
                        lineToEnemy.SetRefCoord(playerPosition); // Start point at player.
                        lineToEnemy.SetOffCoord(gameObject.Position); // End point at enemy.
                        lineToEnemy.color = 0xFFFFFFFF; // Example: White color.
                        lineToEnemy.thicc = 2.0f; // Line thickness.
        
                        // Display the line for one frame.
                        Splatoon.DisplayOnce(lineToEnemy);
                    }
                }
            }
        }
    }
}
