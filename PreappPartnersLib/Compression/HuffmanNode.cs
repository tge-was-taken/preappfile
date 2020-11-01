using System.Linq;
using System.Text;

namespace PreappPartnersLib.Compression
{
    internal class HuffmanNode
    {
        private HuffmanNode right;
        private HuffmanNode left;
        private byte? value;

        public HuffmanNode Parent { get; set; }
        public byte? Value
        {
            get => value;
            set
            {
                this.value = value;
                Validate();
            }
        }
        public HuffmanNode Left
        {
            get => left;
            set
            {
                left = value;
                if (left != null) left.Parent = this;
                Validate();
            }
        }

        public HuffmanNode Right
        {
            get => right;
            set
            {
                right = value;
                if (right != null) right.Parent = this;
                Validate();
            }
        }
        public bool IsInvalid { get; set; }

        public int Index { get; set; }

        public static int Counter = 0;

        public HuffmanNode()
        {
            IsInvalid = true;
            Index = Counter++;
        }

        // Find first partial node, that is a node that is not a leaf and does not have children
        public HuffmanNode FindFirstInvalidNode()
        {
            if (/*Value == null && Left == null && Right == null*/ IsInvalid)
                return this;

            if (Left != null)
            {
                var found = Left.FindFirstInvalidNode();
                if (found != null)
                    return found;
            }

            if (Right != null)
            {
                var found = Right.FindFirstInvalidNode();
                if (found != null)
                    return found;
            }

            return null;
        }

        public void Print(StringBuilder builder, string prefix, string childrenPrefix)
        {
            builder.Append(prefix);
            builder.Append(Value != null ? Value.ToString() : $"#{Index}");
            builder.Append('\n');

            if (Left != null)
            {
                if (Right != null)
                    Left.Print(builder, childrenPrefix + "├── ", childrenPrefix + "│   ");
                else
                    Left.Print(builder, childrenPrefix + "└── ", childrenPrefix + "    ");
            }

            if (Right != null)
            {
                Right.Print(builder, childrenPrefix + "└── ", childrenPrefix + "    ");
            }
        }

        public void Invalidate()
        {
            IsInvalid = true;
            Left?.Invalidate();
            Right?.Invalidate();
        }

        private void Validate()
        {
            IsInvalid = Value == null && Left == null && Right == null;
        }
    }
}
