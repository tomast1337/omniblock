namespace OmniBlock.Rules;

internal interface IRulesProvider
{
    void RegisterAll(RuleRegistry registry);
}
