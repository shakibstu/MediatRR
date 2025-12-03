using VoidType = MediatRR.Contract.Messaging.Void;

namespace MediatRR.Tests
{
    public class VoidTests
    {
        [Fact]
        public void Void_ShouldBeEqual()
        {
            var v1 = VoidType.Value;
            var v2 = new VoidType();

            Assert.Equal(v1, v2);
            Assert.True(v1 == v2);
            Assert.False(v1 != v2);
            Assert.Equal(0, v1.CompareTo(v2));
            Assert.Equal("()", v1.ToString());
        }

        [Fact]
        public void Void_GetHashCode_ShouldReturnZero()
        {
            Assert.Equal(0, VoidType.Value.GetHashCode());
        }

        [Fact]
        public void Void_EqualsObject_ShouldReturnTrueForVoid()
        {
            object obj = new VoidType();
            Assert.True(VoidType.Value.Equals(obj));
            Assert.False(VoidType.Value.Equals(new object()));
            Assert.False(VoidType.Value.Equals(null));
        }

        [Fact]
        public void Void_CompareToObject_ShouldReturnZero()
        {
            IComparable comparable = VoidType.Value;
            Assert.Equal(0, comparable.CompareTo(new object()));
        }

        [Fact]
        public async System.Threading.Tasks.Task Void_Task_ShouldReturnCompletedTaskWithValue()
        {
            var task = VoidType.Task;
            Assert.True(task.IsCompletedSuccessfully);
            Assert.Equal(VoidType.Value, await task);
        }
    }
}
