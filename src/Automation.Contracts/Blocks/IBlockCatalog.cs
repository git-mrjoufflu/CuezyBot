namespace Automation.Contracts.Blocks;

public interface IBlockCatalog
{
    IReadOnlyCollection<BlockDescriptor> GetAll();

    BlockDescriptor? Find(string type, int version);
}
