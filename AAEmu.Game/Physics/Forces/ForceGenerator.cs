using Jitter;

namespace AAEmu.Game.Physics.Forces
{
    /// <summary>
    /// Base class for physic effect.
    /// </summary>
    public class ForceGenerator
    {

        /// <summary>
        /// 
        /// </summary>
        protected World world;

        private World.WorldStep preStep, postStep;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="world"></param>
        public ForceGenerator(World world)
        {
            this.world = world;

            preStep = new World.WorldStep(PreStep);
            postStep = new World.WorldStep(PostStep);

            world.Events.PostStep += postStep;
            world.Events.PreStep += preStep;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="timeStep"></param>
        public virtual void PreStep(float timeStep)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="timeStep"></param>
        public virtual void PostStep(float timeStep)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        public void RemoveEffect()
        {
            world.Events.PostStep -= postStep;
            world.Events.PreStep -= preStep;
        }


    }
}
