using betareborn.Worlds.Storage;
using java.text;
using java.util;

namespace betareborn.Guis
{
    public class GuiSelectWorld : GuiScreen
    {

        private readonly DateFormat dateFormatter = new SimpleDateFormat();
        protected GuiScreen parentScreen;
        protected String screenTitle = "Select world";
        private bool selected = false;
        private int selectedWorld;
        private List saveList;
        private GuiWorldSlot worldSlotContainer;
        private String field_22098_o;
        private String field_22097_p;
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
            StringTranslate var1 = StringTranslate.getInstance();
            screenTitle = var1.translateKey("selectWorld.title");
            field_22098_o = var1.translateKey("selectWorld.world");
            field_22097_p = var1.translateKey("selectWorld.conversion");
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

        protected String getSaveFileName(int var1)
        {
            return ((WorldSaveInfo)saveList.get(var1)).getFileName();
        }

        protected String getSaveName(int var1)
        {
            String var2 = ((WorldSaveInfo)saveList.get(var1)).getDisplayName();
            if (var2 == null || MathHelper.stringNullOrLengthZero(var2))
            {
                StringTranslate var3 = StringTranslate.getInstance();
                var2 = var3.translateKey("selectWorld.world") + " " + (var1 + 1);
            }

            return var2;
        }

        public void initButtons()
        {
            StringTranslate var1 = StringTranslate.getInstance();
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
                    String var2 = getSaveName(selectedWorld);
                    if (var2 != null)
                    {
                        deleting = true;
                        StringTranslate var3 = StringTranslate.getInstance();
                        String var4 = var3.translateKey("selectWorld.deleteQuestion");
                        String var5 = "\'" + var2 + "\' " + var3.translateKey("selectWorld.deleteWarning");
                        String var6 = var3.translateKey("selectWorld.deleteButton");
                        String var7 = var3.translateKey("gui.cancel");
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
            mc.displayGuiScreen((GuiScreen)null);
            if (!selected)
            {
                selected = true;
                mc.playerController = new PlayerControllerSP(mc);
                String var2 = getSaveFileName(var1);
                if (var2 == null)
                {
                    var2 = "World" + var1;
                }

                mc.startWorld(var2, getSaveName(var1), 0L);
                mc.displayGuiScreen((GuiScreen)null);
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

        public override void drawScreen(int var1, int var2, float var3)
        {
            worldSlotContainer.drawScreen(var1, var2, var3);
            drawCenteredString(fontRenderer, screenTitle, width / 2, 20, 16777215);
            base.drawScreen(var1, var2, var3);
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

        public static String func_22087_f(GuiSelectWorld var0)
        {
            return var0.field_22098_o;
        }

        public static DateFormat getDateFormatter(GuiSelectWorld var0)
        {
            return var0.dateFormatter;
        }

        public static String func_22088_h(GuiSelectWorld var0)
        {
            return var0.field_22097_p;
        }
    }

}