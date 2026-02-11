using betareborn.Client.Resource.Language;
using betareborn.Util.Maths;
using betareborn.Worlds.Storage;
using java.text;
using java.util;

namespace betareborn.Client.Guis
{
    public class GuiSelectWorld : GuiScreen
    {

        private readonly DateFormat dateFormatter = new SimpleDateFormat();
        protected GuiScreen parentScreen;
        protected string screenTitle = "Select world";
        private bool selected = false;
        private int selectedWorld;
        private List saveList;
        private GuiWorldSlot worldSlotContainer;
        private string worldNameHeader;
        private string unsupportedFormatMessage;
        private bool deleting;
        private GuiButton buttonRename;
        private GuiButton buttonSelect;
        private GuiButton buttonDelete;

        public GuiSelectWorld(GuiScreen var1)
        {
            parentScreen = var1;
        }

        public override void initGui()
        {
            TranslationStorage var1 = TranslationStorage.getInstance();
            screenTitle = var1.translateKey("selectWorld.title");
            worldNameHeader = var1.translateKey("selectWorld.world");
            unsupportedFormatMessage = "Unsupported Format!";
            loadSaves();
            worldSlotContainer = new GuiWorldSlot(this);
            worldSlotContainer.registerScrollButtons(controlList, 4, 5);
            initButtons();
        }

        private void loadSaves()
        {
            WorldStorageSource var1 = mc.getSaveLoader();
            saveList = var1.getAll();
            Collections.sort(saveList);
            selectedWorld = -1;
        }

        protected string getSaveFileName(int var1)
        {
            return ((WorldSaveInfo)saveList.get(var1)).getFileName();
        }

        protected string getSaveName(int var1)
        {
            string var2 = ((WorldSaveInfo)saveList.get(var1)).getDisplayName();
            if (var2 == null || MathHelper.stringNullOrLengthZero(var2))
            {
                TranslationStorage var3 = TranslationStorage.getInstance();
                var2 = var3.translateKey("selectWorld.world") + " " + (var1 + 1);
            }

            return var2;
        }

        public void initButtons()
        {
            TranslationStorage var1 = TranslationStorage.getInstance();
            controlList.add(buttonSelect = new GuiButton(1, width / 2 - 154, height - 52, 150, 20, var1.translateKey("selectWorld.select")));
            controlList.add(buttonRename = new GuiButton(6, width / 2 - 154, height - 28, 70, 20, var1.translateKey("selectWorld.rename")));
            controlList.add(buttonDelete = new GuiButton(2, width / 2 - 74, height - 28, 70, 20, var1.translateKey("selectWorld.delete")));
            controlList.add(new GuiButton(3, width / 2 + 4, height - 52, 150, 20, var1.translateKey("selectWorld.create")));
            controlList.add(new GuiButton(0, width / 2 + 4, height - 28, 150, 20, var1.translateKey("gui.cancel")));
            buttonSelect.enabled = false;
            buttonRename.enabled = false;
            buttonDelete.enabled = false;
        }

        protected override void actionPerformed(GuiButton var1)
        {
            if (var1.enabled)
            {
                if (var1.id == 2)
                {
                    string var2 = getSaveName(selectedWorld);
                    if (var2 != null)
                    {
                        deleting = true;
                        TranslationStorage var3 = TranslationStorage.getInstance();
                        string var4 = var3.translateKey("selectWorld.deleteQuestion");
                        string var5 = "\'" + var2 + "\' " + var3.translateKey("selectWorld.deleteWarning");
                        string var6 = var3.translateKey("selectWorld.deleteButton");
                        string var7 = var3.translateKey("gui.cancel");
                        GuiYesNo var8 = new GuiYesNo(this, var4, var5, var6, var7, selectedWorld);
                        mc.displayGuiScreen(var8);
                    }
                }
                else if (var1.id == 1)
                {
                    selectWorld(selectedWorld);
                }
                else if (var1.id == 3)
                {
                    mc.displayGuiScreen(new GuiCreateWorld(this));
                }
                else if (var1.id == 6)
                {
                    mc.displayGuiScreen(new GuiRenameWorld(this, getSaveFileName(selectedWorld)));
                }
                else if (var1.id == 0)
                {
                    mc.displayGuiScreen(parentScreen);
                }
                else
                {
                    worldSlotContainer.actionPerformed(var1);
                }

            }
        }

        public void selectWorld(int var1)
        {
            if (!selected)
            {
                selected = true;
                mc.playerController = new PlayerControllerSP(mc);
                string var2 = getSaveFileName(var1);
                if (var2 == null)
                {
                    var2 = "World" + var1;
                }

                mc.startWorld(var2, getSaveName(var1), 0L);
            }
        }

        public override void deleteWorld(bool var1, int var2)
        {
            if (deleting)
            {
                deleting = false;
                if (var1)
                {
                    WorldStorageSource var3 = mc.getSaveLoader();
                    var3.flush();
                    var3.delete(getSaveFileName(var2));
                    loadSaves();
                }

                mc.displayGuiScreen(this);
            }

        }

        public override void render(int var1, int var2, float var3)
        {
            worldSlotContainer.drawScreen(var1, var2, var3);
            drawCenteredString(fontRenderer, screenTitle, width / 2, 20, 16777215);
            base.render(var1, var2, var3);
        }

        public static List getSize(GuiSelectWorld var0)
        {
            return var0.saveList;
        }

        public static int onElementSelected(GuiSelectWorld var0, int var1)
        {
            return var0.selectedWorld = var1;
        }

        public static int getSelectedWorld(GuiSelectWorld var0)
        {
            return var0.selectedWorld;
        }

        public static GuiButton getSelectButton(GuiSelectWorld var0)
        {
            return var0.buttonSelect;
        }

        public static GuiButton getRenameButton(GuiSelectWorld var0)
        {
            return var0.buttonRename;
        }

        public static GuiButton getDeleteButton(GuiSelectWorld var0)
        {
            return var0.buttonDelete;
        }

        public static string getWorldNameHeader(GuiSelectWorld var0)
        {
            return var0.worldNameHeader;
        }

        public static DateFormat getDateFormatter(GuiSelectWorld var0)
        {
            return var0.dateFormatter;
        }

        public static string getUnsupportedFormatMessage(GuiSelectWorld var0)
        {
            return var0.unsupportedFormatMessage;
        }
    }

}