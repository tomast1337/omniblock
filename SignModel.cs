using betareborn.Client.Rendering.Entitys.Models;

namespace betareborn
{
    public class SignModel
    {
        public ModelPart signBoard = new ModelPart(0, 0);
        public ModelPart signStick;

        public SignModel()
        {
            signBoard.addBox(-12.0F, -14.0F, -1.0F, 24, 12, 2, 0.0F);
            signStick = new ModelPart(0, 14);
            signStick.addBox(-1.0F, -2.0F, -1.0F, 2, 14, 2, 0.0F);
        }

        public void func_887_a()
        {
            signBoard.render(1.0F / 16.0F);
            signStick.render(1.0F / 16.0F);
        }
    }

}
