namespace BackgroundEmailSenderSample.Models.ValueTypes
{
    public class Sql 
    {
        private Sql(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public static explicit operator Sql(string value) => new Sql(value);
        public override string ToString() {
            return this.Value;
        }
    }
}