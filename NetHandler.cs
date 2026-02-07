using betareborn.Packets;

namespace betareborn
{
    public abstract class NetHandler
    {
        public abstract bool isServerHandler();

        public virtual void handleMapChunk(Packet51MapChunk var1)
        {
        }

        public virtual void registerPacket(Packet var1)
        {
        }

        public virtual void handleErrorMessage(String var1, Object[] var2)
        {
        }

        public virtual void handleKickDisconnect(Packet255KickDisconnect var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleLogin(Packet1Login var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleFlying(Packet10Flying var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleMultiBlockChange(Packet52MultiBlockChange var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleBlockDig(Packet14BlockDig var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleBlockChange(Packet53BlockChange var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handlePreChunk(Packet50PreChunk var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleNamedEntitySpawn(Packet20NamedEntitySpawn var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleEntity(Packet30Entity var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleEntityTeleport(Packet34EntityTeleport var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handlePlace(Packet15Place var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleBlockItemSwitch(Packet16BlockItemSwitch var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleDestroyEntity(Packet29DestroyEntity var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handlePickupSpawn(Packet21PickupSpawn var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleCollect(Packet22Collect var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleChat(Packet3Chat var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleVehicleSpawn(Packet23VehicleSpawn var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleArmAnimation(Packet18Animation var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_21147_a(Packet19EntityAction var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleHandshake(Packet2Handshake var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleMobSpawn(Packet24MobSpawn var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleUpdateTime(Packet4UpdateTime var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleSpawnPosition(Packet6SpawnPosition var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_6498_a(Packet28EntityVelocity var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_21148_a(Packet40EntityMetadata var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_6497_a(Packet39AttachEntity var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleUseEntity(Packet7UseEntity var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_9447_a(Packet38EntityStatus var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleHealth(Packet8UpdateHealth var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_9448_a(Packet9Respawn var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_12245_a(Packet60Explosion var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_20087_a(Packet100OpenWindow var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_20092_a(Packet101CloseWindow var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_20091_a(Packet102WindowClick var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_20088_a(Packet103SetSlot var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_20094_a(Packet104WindowItems var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleSignUpdate(UpdateSignPacket var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_20090_a(Packet105UpdateProgressbar var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handlePlayerInventory(Packet5PlayerInventory var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_20089_a(Packet106Transaction var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_21146_a(Packet25EntityPainting var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleNotePlay(Packet54PlayNoteBlock var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_27245_a(Packet200Statistic var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_22186_a(Packet17Sleep var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_22185_a(Packet27Position var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_25118_a(Packet70Bed var1)
        {
            this.registerPacket(var1);
        }

        public virtual void handleWeather(Packet71Weather var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_28116_a(Packet131MapData var1)
        {
            this.registerPacket(var1);
        }

        public virtual void func_28115_a(Packet61DoorChange var1)
        {
            this.registerPacket(var1);
        }
    }

}